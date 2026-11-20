using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

sealed class DesktopChatConnection
{
    const int MaxFrame = 64 * 1024 * 1024;
    readonly string pipeName;
    readonly Action<NamedPipeClientStream> verifyServer;

    internal DesktopChatConnection() : this("codex-ipc", VerifyDesktopServer) { }

    internal DesktopChatConnection(string pipeName, Action<NamedPipeClientStream> verifyServer)
    {
        this.pipeName = pipeName;
        this.verifyServer = verifyServer;
    }

    internal sealed class UnconfirmedSendException : IOException
    {
        internal UnconfirmedSendException(Exception cause)
            : base("Delivery unconfirmed · Check this chat in Codex Desktop before resending.", cause) { }
    }

    internal sealed class LiveTurn
    {
        internal readonly string Id, Status, Answer;
        internal LiveTurn(string id, string status, string answer) { Id = id; Status = status; Answer = answer; }
    }

    internal async Task FollowAsync(string threadId, Action<LiveTurn> changed, CancellationToken stop)
    {
        using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
        using (stop.Register(pipe.Dispose))
        {
            await pipe.ConnectAsync(2000).ConfigureAwait(false);
            verifyServer(pipe);
            var json = new JavaScriptSerializer { MaxJsonLength = MaxFrame };
            var hello = await RequestAsync(pipe, json, "initializing-client", "initialize", 0,
                new { clientType = "chat-craft" }).ConfigureAwait(false);
            string clientId = Text(Map(Field(hello, "result")), "clientId");
            if (String.IsNullOrEmpty(clientId))
                return;
            var discovery = await RequestAsync(pipe, json, clientId, "thread-owner-discovery", 1,
                new { hostId = "local", conversationId = threadId }).ConfigureAwait(false);
            string owner = Text(discovery, "handledByClientId");
            if (String.IsNullOrEmpty(owner) || owner == clientId)
                return;

            await WriteAsync(pipe, json, new
            {
                type = "broadcast", method = "thread-stream-following-changed", sourceClientId = clientId,
                version = 1, targetClientIds = new[] { owner },
                @params = new { hostId = "local", conversationId = threadId, following = true }
            }).ConfigureAwait(false);
            object state = null;
            int revision = -1;
            LiveTurn last = null;
            while (!stop.IsCancellationRequested)
            {
                var message = await ReadFrameAsync(pipe, json).ConfigureAwait(false);
                if (await AnswerIncomingAsync(pipe, json, message).ConfigureAwait(false))
                    continue;
                if (Text(message, "type") != "broadcast" || Text(message, "sourceClientId") != owner ||
                    Text(message, "method") != "thread-stream-state-changed" || !Equals(Field(message, "version"), 13))
                    continue;
                var parameters = Map(Field(message, "params"));
                if (Text(parameters, "conversationId") != threadId || Text(parameters, "hostId") != "local")
                    continue;
                var change = Map(Field(parameters, "change"));
                if (!(Field(change, "revision") is int))
                    continue;
                if (Text(change, "type") == "snapshot")
                    state = Mutable(Field(change, "conversationState"));
                else if (Text(change, "type") == "patches")
                {
                    if (state == null || !Equals(Field(change, "baseRevision"), revision))
                        return; // Fall back to saved turns if the live feed cannot be joined safely.
                    var patches = Field(change, "patches") as IList;
                    if (patches == null)
                        return;
                    foreach (object value in patches)
                        if (!ApplyPatch(ref state, Map(value)))
                            return;
                }
                else
                    continue;
                revision = (int)Field(change, "revision");
                var turn = CurrentTurn(Map(state));
                if (turn != null && (last == null || last.Id != turn.Id || last.Status != turn.Status ||
                    last.Answer != turn.Answer))
                {
                    last = turn;
                    changed(turn);
                }
            }
        }
    }

    static LiveTurn CurrentTurn(Dictionary<string, object> state)
    {
        var history = Map(Field(Map(Field(state, "turnHistory")), "history"));
        var entities = Map(Field(history, "entitiesByKey"));
        var islands = Field(history, "islands") as IList;
        Dictionary<string, object> turn = null;
        if (islands != null && islands.Count > 0)
        {
            var entries = Field(Map(islands[islands.Count - 1]), "entries") as IList;
            if (entries != null && entries.Count > 0)
            {
                string key = Text(Map(entries[entries.Count - 1]), "value");
                if (key != null)
                    turn = Map(Field(entities, key));
            }
        }
        var legacy = Field(state, "turns") as IList;
        if (turn == null && legacy != null && legacy.Count > 0)
            turn = Map(legacy[legacy.Count - 1]);
        string id = Text(turn, "turnId"), status = Text(turn, "status");
        if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(status))
            return null;
        string answer = null;
        var items = Field(turn, "items") as IList;
        if (items != null)
            foreach (object value in items)
            {
                var item = Map(value);
                if (Text(item, "type") == "agentMessage" && Text(item, "phase") == "final_answer")
                    answer = Text(item, "text");
            }
        return new LiveTurn(id, status, answer);
    }

    static object Mutable(object value)
    {
        var map = Map(value);
        if (map != null)
        {
            var result = new Dictionary<string, object>();
            foreach (var pair in map)
                result.Add(pair.Key, Mutable(pair.Value));
            return result;
        }
        var list = value as IList;
        if (list == null)
            return value;
        var array = new ArrayList(list.Count);
        foreach (object item in list)
            array.Add(Mutable(item));
        return array;
    }

    static bool ApplyPatch(ref object state, Dictionary<string, object> patch)
    {
        var path = Field(patch, "path") as IList;
        string operation = Text(patch, "op");
        if (path == null || (operation != "replace" && operation != "add" && operation != "remove"))
            return false;
        object value = Mutable(Field(patch, "value"));
        if (path.Count == 0)
        {
            state = value;
            return operation == "replace";
        }
        object parent = state;
        for (int index = 0; index < path.Count - 1; index++)
        {
            var list = parent as IList;
            if (list != null && path[index] is int && (int)path[index] >= 0 && (int)path[index] < list.Count)
                parent = list[(int)path[index]];
            else if (path[index] is string)
                parent = Field(Map(parent), (string)path[index]);
            else
                return false;
        }
        object part = path[path.Count - 1];
        var map = Map(parent);
        if (map != null && part is string)
        {
            if (operation == "remove") return map.Remove((string)part);
            map[(string)part] = value;
            return true;
        }
        var array = parent as IList;
        if (array == null || !(part is int))
            return false;
        int itemIndex = (int)part;
        if (itemIndex < 0 || itemIndex > array.Count || (operation != "add" && itemIndex == array.Count))
            return false;
        if (operation == "add") array.Insert(itemIndex, value);
        else if (operation == "remove") array.RemoveAt(itemIndex);
        else array[itemIndex] = value;
        return true;
    }

    internal async Task<string> SendAsync(string threadId, string prompt)
    {
        using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
        {
            try
            {
                await pipe.ConnectAsync(2000).ConfigureAwait(false);
                verifyServer(pipe);
            }
            catch (Exception error)
            {
                throw new IOException("Could not connect to the installed Codex Desktop · Draft kept.", error);
            }

            var json = new JavaScriptSerializer { MaxJsonLength = MaxFrame };
            var registration = await RequestAsync(pipe, json, "initializing-client", "initialize", 0,
                new { clientType = "chat-craft" }).ConfigureAwait(false);
            string clientId = Text(Map(Field(registration, "result")), "clientId");
            if (String.IsNullOrWhiteSpace(clientId))
                throw new IOException("Codex Desktop did not register the connection · Draft kept.");

            var discovery = await RequestAsync(pipe, json, clientId, "thread-owner-discovery", 1,
                new { hostId = "local", conversationId = threadId }).ConfigureAwait(false);
            string ownerId = Text(discovery, "handledByClientId");
            if (String.IsNullOrWhiteSpace(ownerId) || ownerId == clientId)
                throw new IOException("Could not identify the Desktop window for this chat · Draft kept.");

            // The owner prepares and submits the turn with its existing permissions and approval settings.
            try
            {
                var sent = await RequestAsync(pipe, json, clientId, "thread-follower-start-turn", 2,
                    new
                    {
                        conversationId = threadId,
                        turnStart = new
                        {
                            request = new
                            {
                                threadId,
                                clientUserMessageId = Guid.NewGuid().ToString(),
                                input = new[] { new { type = "text", text = prompt, text_elements = new object[0] } }
                            },
                            context = new { inheritThreadSettings = true }
                        }
                    }, ownerId, 35000).ConfigureAwait(false);
                var result = Map(Field(Map(Field(sent, "result")), "result"));
                string turnId = Text(Map(Field(result, "turn")), "id");
                if (String.IsNullOrWhiteSpace(turnId))
                    throw new IOException("The Desktop receipt did not include a turn ID.");
                return turnId;
            }
            catch (Exception error)
            {
                throw new UnconfirmedSendException(error);
            }
        }
    }

    static async Task<Dictionary<string, object>> RequestAsync(NamedPipeClientStream pipe,
        JavaScriptSerializer json, string clientId, string method, int version, object parameters,
        string targetClientId = null, int timeoutMs = 4500)
    {
        string requestId = Guid.NewGuid().ToString();
        var request = new Dictionary<string, object>
        {
            { "type", "request" }, { "requestId", requestId }, { "sourceClientId", clientId },
            { "version", version }, { "method", method }, { "params", parameters }, { "timeoutMs", timeoutMs }
        };
        if (targetClientId != null)
            request.Add("targetClientId", targetClientId);

        using (var deadline = new CancellationTokenSource(timeoutMs + 750))
        using (deadline.Token.Register(pipe.Dispose))
        {
            await WriteAsync(pipe, json, request).ConfigureAwait(false);
            while (true)
            {
                var message = await ReadFrameAsync(pipe, json).ConfigureAwait(false);
                string type = Text(message, "type"), id = Text(message, "requestId");
                if (await AnswerIncomingAsync(pipe, json, message).ConfigureAwait(false))
                    continue;
                if (type == "response" && id == requestId)
                {
                    if (Text(message, "resultType") != "success")
                        throw new IOException(Text(message, "error") == "no-client-found"
                            ? "Codex Desktop is not hosting this chat right now · Draft kept."
                            : "Codex Desktop did not accept the request · Draft kept.");
                    if (targetClientId != null && Text(message, "handledByClientId") != targetClientId)
                        throw new IOException("The receipt came from a different Desktop window.");
                    return message;
                }
            }
        }
    }

    static async Task<bool> AnswerIncomingAsync(Stream pipe, JavaScriptSerializer json, Dictionary<string, object> message)
    {
        string id = Text(message, "requestId");
        string type = Text(message, "type");
        if (id == null)
            return false;
        if (type == "client-discovery-request")
            await WriteAsync(pipe, json, new { type = "client-discovery-response", requestId = id,
                response = new { canHandle = false } }).ConfigureAwait(false);
        else if (type == "request")
            await WriteAsync(pipe, json, new { type = "response", requestId = id,
                resultType = "error", error = "no-handler-for-request" }).ConfigureAwait(false);
        else
            return false;
        return true;
    }

    static async Task<Dictionary<string, object>> ReadFrameAsync(Stream pipe, JavaScriptSerializer json)
    {
        var header = new byte[4];
        await ReadExactlyAsync(pipe, header).ConfigureAwait(false);
        int length = BitConverter.ToInt32(header, 0);
        if (length <= 0 || length > MaxFrame)
            throw new IOException("Invalid Codex Desktop message size.");
        var bytes = new byte[length];
        await ReadExactlyAsync(pipe, bytes).ConfigureAwait(false);
        return json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(bytes));
    }

    static async Task ReadExactlyAsync(Stream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int received = await stream.ReadAsync(buffer, offset, buffer.Length - offset).ConfigureAwait(false);
            if (received == 0)
                throw new EndOfStreamException("The Codex Desktop connection closed.");
            offset += received;
        }
    }

    static Task WriteAsync(Stream stream, JavaScriptSerializer json, object value)
    {
        var body = Encoding.UTF8.GetBytes(json.Serialize(value));
        if (body.Length > MaxFrame)
            throw new IOException("Codex Desktop message exceeds the size limit.");
        var frame = new byte[4 + body.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(body.Length), 0, frame, 0, 4);
        Buffer.BlockCopy(body, 0, frame, 4, body.Length);
        return stream.WriteAsync(frame, 0, frame.Length);
    }

    static Dictionary<string, object> Map(object value) { return CodexCraftClient.Map(value); }
    static object Field(Dictionary<string, object> value, string name) { return CodexCraftClient.Field(value, name); }
    static string Text(Dictionary<string, object> value, string name) { return CodexCraftClient.Text(value, name); }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetNamedPipeServerProcessId(SafePipeHandle handle, out uint processId);

    static void VerifyDesktopServer(NamedPipeClientStream pipe)
    {
        uint processId;
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out processId))
            throw new IOException("Could not verify the local Codex Desktop process.");
        using (var process = Process.GetProcessById((int)processId))
        {
            string exe = process.MainModule.FileName;
            string app = Path.GetDirectoryName(exe);
            string package = Path.GetDirectoryName(app);
            string name = Path.GetFileName(package);
            var installedApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            const StringComparison match = StringComparison.OrdinalIgnoreCase;
            if (!String.Equals(Path.GetFileName(exe), "ChatGPT.exe", match) ||
                !String.Equals(Path.GetFileName(app), "app", match) ||
                !String.Equals(Path.GetDirectoryName(package), installedApps, match) ||
                !name.StartsWith("OpenAI.Codex_", match) || !name.EndsWith("__2p2nqsd0c76g0", match))
                throw new IOException("The local message endpoint is not provided by the installed Codex Desktop.");
        }
    }
}
