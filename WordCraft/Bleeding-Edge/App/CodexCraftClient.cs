using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

sealed class CodexCraftClient : IDisposable
{
    readonly object lifetime = new object();
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
    readonly string executable, workspace;
    Process process;
    Chat selectedChat;
    string loadedThreadId;
    int sequence;
    bool disposed;

    internal CodexCraftClient(string executable, string workspace)
    {
        this.executable = executable;
        this.workspace = Path.GetFullPath(workspace);
    }

    internal sealed class Chat
    {
        internal readonly string Id, Title, Folder;

        internal Chat(Dictionary<string, object> thread, string firstPrompt = null)
        {
            Id = Text(thread, "id");
            Folder = Text(thread, "cwd") ?? "";
            string title = Text(thread, "name");
            if (String.IsNullOrWhiteSpace(title))
                title = Text(thread, "preview");
            if (String.IsNullOrWhiteSpace(title))
                title = firstPrompt;
            title = String.IsNullOrWhiteSpace(title) ? "Untitled chat" :
                System.Text.RegularExpressions.Regex.Replace(title.Trim(), @"\s+", " ");
            Title = title.Length <= 110 ? title : title.Substring(0, 109) + "…";
        }

        public override string ToString() { return Folder.Length == 0 ? Title : Title + "\n" + Folder; }
    }

    internal sealed class ChatPage
    {
        internal readonly List<Chat> Chats = new List<Chat>();
        internal string NextCursor;
    }

    internal sealed class SavedReply
    {
        internal readonly string TurnId, Text, Status;
        internal readonly bool HasAnswerText;
        internal bool IsActive { get { return Status == "inProgress"; } }

        internal SavedReply(string turnId, string text, string status, bool hasAnswerText = false)
        {
            TurnId = turnId;
            Text = text;
            Status = status;
            HasAnswerText = hasAnswerText;
        }
    }

    internal Chat SelectedChat { get { return selectedChat; } }

    internal void NewChat()
    {
        selectedChat = null;
        loadedThreadId = null;
    }

    internal async Task<ChatPage> ListChatsAsync(string cursor, string search)
    {
        try
        {
            var connection = await ConnectAsync().ConfigureAwait(false);
            var result = await Request(connection, "thread/list", new
            {
                cursor, limit = 40, sortKey = "updated_at", archived = false,
                searchTerm = String.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                sourceKinds = new[] { "cli", "vscode", "appServer" }
            }).ConfigureAwait(false);
            var page = new ChatPage { NextCursor = Text(result, "nextCursor") };
            var rows = Field(result, "data") as System.Collections.IList;
            if (rows != null)
                foreach (object row in rows)
                {
                    var entry = Map(row);
                    var chat = new Chat(entry);
                    if (!String.IsNullOrWhiteSpace(chat.Id) && !Equals(Field(entry, "ephemeral"), true))
                        page.Chats.Add(chat);
                }
            return page;
        }
        catch (Exception error)
        {
            if (!(error is InvalidOperationException))
                Reset();
            throw;
        }
    }

    internal async Task SelectChatAsync(Chat chat)
    {
        try
        {
            var connection = await ConnectAsync().ConfigureAwait(false);
            selectedChat = await ResumeChat(connection, chat).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (!(error is InvalidOperationException))
                Reset();
            throw;
        }
    }

    internal async Task<SavedReply> ReadLatestReplyAsync(Chat chat, string expectedTurnId = null,
        bool observeLatestTurn = false)
    {
        try
        {
            var connection = await ConnectAsync().ConfigureAwait(false);
            var result = await Request(connection, "thread/turns/list", new
            {
                threadId = chat.Id, limit = 4, sortDirection = "desc", itemsView = "full"
            }).ConfigureAwait(false);
            var turns = Field(result, "data") as System.Collections.IList;
            if (turns == null)
                return null;
            foreach (object row in turns)
            {
                var turn = Map(row);
                string id = Text(turn, "id");
                string status = Text(turn, "status");
                if (String.IsNullOrEmpty(id) || (expectedTurnId != null && id != expectedTurnId))
                    continue;
                var items = Field(turn, "items") as System.Collections.IList;
                if (status != "completed")
                {
                    if (expectedTurnId != null || observeLatestTurn)
                    {
                        bool hasAnswerText = false;
                        if (status == "inProgress" && items != null)
                            foreach (object value in items)
                            {
                                var item = Map(value);
                                string phase = Text(item, "phase");
                                if (Text(item, "type") == "agentMessage" &&
                                    (phase == "final_answer" || phase == "partial_answer") &&
                                    !String.IsNullOrWhiteSpace(Text(item, "text")))
                                {
                                    hasAnswerText = true;
                                    break;
                                }
                            }
                        return new SavedReply(id, null, status, hasAnswerText);
                    }
                    continue;
                }
                var reply = new Reply(chat.Id);
                if (items != null)
                    foreach (object value in items)
                        reply.AcceptItem(Map(value));
                string content = reply.Content;
                if (expectedTurnId != null || observeLatestTurn || !String.IsNullOrWhiteSpace(content))
                    return new SavedReply(id, content, status);
            }
            return null;
        }
        catch (Exception error)
        {
            if (!(error is InvalidOperationException))
                Reset();
            throw;
        }
    }

    async Task<Chat> ResumeChat(Process connection, Chat chat)
    {
        var response = await Request(connection, "thread/resume", new
        {
            threadId = chat.Id, excludeTurns = true, sandbox = "read-only", approvalPolicy = "never"
        }).ConfigureAwait(false);
        var thread = Map(Field(response, "thread"));
        if (Text(thread, "id") != chat.Id)
            throw new IOException("Codex returned a different conversation.");
        if (Text(Map(Field(thread, "status")), "type") == "active")
            throw new InvalidOperationException("That chat is running. Wait for its current reply before switching.");
        loadedThreadId = chat.Id;
        return chat;
    }

    internal async Task<string> SendAsync(string prompt)
    {
        try
        {
            var connection = await ConnectAsync().ConfigureAwait(false);
            if (selectedChat == null)
            {
                var started = await Request(connection, "thread/start", new
                {
                    cwd = workspace, sandbox = "read-only", approvalPolicy = "never", ephemeral = false
                }).ConfigureAwait(false);
                var chat = new Chat(Map(Field(started, "thread")), prompt);
                if (String.IsNullOrEmpty(chat.Id))
                    throw new IOException("Codex did not return a conversation ID.");
                selectedChat = chat;
                loadedThreadId = chat.Id;
            }
            else if (loadedThreadId != selectedChat.Id)
                selectedChat = await ResumeChat(connection, selectedChat).ConfigureAwait(false);

            string threadId = selectedChat.Id;
            var turn = new Reply(threadId);
            var response = await Request(connection, "turn/start", new
            {
                threadId, input = new[] { new { type = "text", text = prompt } },
                approvalPolicy = "never", sandboxPolicy = new { type = "readOnly" }
            }, turn.Accept).ConfigureAwait(false);
            turn.Id = Text(Map(Field(response, "turn")), "id");
            if (String.IsNullOrEmpty(turn.Id))
                throw new IOException("Codex did not return a turn ID.");
            while (!turn.Finished)
            {
                var message = await Read(connection, 180000).ConfigureAwait(false);
                await RejectServerRequest(connection, message).ConfigureAwait(false);
                turn.Accept(message);
            }
            return turn.Result();
        }
        catch (Exception error)
        {
            // A single failed turn can be retried on its existing conversation. A broken stream cannot.
            if (!(error is InvalidOperationException) || selectedChat == null)
                Reset();
            throw;
        }
    }

    async Task<Process> ConnectAsync()
    {
        bool starting = process == null;
        Process connection = Connection();
        if (!starting)
            return connection;
        try
        {
            await Request(connection, "initialize", new
            {
                clientInfo = new { name = "codexcraft", title = "CodexCraft", version = "0.2.0" }
            }).ConfigureAwait(false);
            await Write(connection, new { method = "initialized", @params = new { } }).ConfigureAwait(false);
            var account = await Request(connection, "account/read", new { refreshToken = false }).ConfigureAwait(false);
            if (Field(account, "account") == null && Equals(Field(account, "requiresOpenaiAuth"), true))
                throw new InvalidOperationException("Codex needs sign-in. Sign in with Codex CLI, then try again.");
            return connection;
        }
        catch
        {
            Reset();
            throw;
        }
    }

    Process Connection()
    {
        lock (lifetime)
        {
            if (disposed)
                throw new ObjectDisposedException("CodexCraft");
            if (process != null)
                return process;
            var info = new ProcessStartInfo(FindExecutable(), "app-server --listen stdio://")
            {
                WorkingDirectory = workspace, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            process = Process.Start(info);
            process.ErrorDataReceived += (sender, args) => { };
            process.BeginErrorReadLine();
            return process;
        }
    }

    string FindExecutable()
    {
        if (!String.IsNullOrWhiteSpace(executable) && Path.IsPathRooted(executable) && File.Exists(executable))
            return executable;
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string entry in path.Split(Path.PathSeparator))
        {
            string folder = entry.Trim().Trim('"');
            if (!Path.IsPathRooted(folder))
                continue;
            string candidate = Path.Combine(folder, "codex.exe");
            if (File.Exists(candidate))
                return candidate;
        }
        string bundled = FindPackagedExecutable();
        if (bundled != null)
            return bundled;
        throw new FileNotFoundException("Codex executable was not found. Open or install Codex Desktop, or supply a valid --codex-exe path.");
    }

    static string FindPackagedExecutable()
    {
        // Query the registered package rather than pinning a WindowsApps version.
        // This runs once per connection, without a console or profile scripts.
        string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(shell)) return null;
        const string query = "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false); Get-AppxPackage -Name OpenAI.Codex | ForEach-Object { $_.InstallLocation }";
        try
        {
            var info = new ProcessStartInfo(shell,
                "-NoLogo -NoProfile -NonInteractive -Command \"" + query + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var lookup = Process.Start(info))
            {
                if (!lookup.WaitForExit(6000))
                {
                    lookup.Kill();
                    return null;
                }
                if (lookup.ExitCode != 0) return null;
                string locations = lookup.StandardOutput.ReadToEnd();
                foreach (string line in locations.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string folder = line.Trim();
                    if (!Path.IsPathRooted(folder)) continue;
                    string candidate = Path.Combine(folder, "app", "resources", "codex.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        catch (System.ComponentModel.Win32Exception error) { Trace.WriteLine(error.Message); }
        catch (InvalidOperationException error) { Trace.WriteLine(error.Message); }
        catch (UnauthorizedAccessException error) { Trace.WriteLine(error.Message); }
        return null;
    }

    async Task<Dictionary<string, object>> Request(Process connection, string method, object parameters,
        Action<Dictionary<string, object>> notification = null)
    {
        int id = ++sequence;
        await Write(connection, new { id, method, @params = parameters }).ConfigureAwait(false);
        while (true)
        {
            var message = await Read(connection, 30000).ConfigureAwait(false);
            if (Text(message, "method") == null && Equals(Field(message, "id"), id))
            {
                var error = Map(Field(message, "error"));
                if (error != null)
                    throw new InvalidOperationException(Text(error, "message") ?? "Codex rejected the request.");
                return Map(Field(message, "result"));
            }
            await RejectServerRequest(connection, message).ConfigureAwait(false);
            if (notification != null)
                notification(message);
        }
    }

    async Task Write(Process connection, object frame)
    {
        // Write UTF-8 explicitly on Windows, where the redirected console code page can differ.
        byte[] bytes = Encoding.UTF8.GetBytes(json.Serialize(frame) + "\n");
        Stream input = connection.StandardInput.BaseStream;
        await input.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        await input.FlushAsync().ConfigureAwait(false);
    }

    async Task<Dictionary<string, object>> Read(Process connection, int timeout)
    {
        Task<string> input = connection.StandardOutput.ReadLineAsync();
        using (var cancelTimeout = new System.Threading.CancellationTokenSource())
        {
            if (await Task.WhenAny(input, Task.Delay(timeout, cancelTimeout.Token)).ConfigureAwait(false) != input)
                throw new TimeoutException("Codex stopped responding. Try again.");
            cancelTimeout.Cancel();
        }
        string line = await input.ConfigureAwait(false);
        if (line == null)
            throw new IOException("The Codex connection ended.");
        return json.Deserialize<Dictionary<string, object>>(line);
    }

    Task RejectServerRequest(Process connection, Dictionary<string, object> message)
    {
        // The first client is explicitly read-only. Unsupported requests must never be approved.
        object id = Field(message, "id");
        if (id == null || Text(message, "method") == null)
            return Task.FromResult(0);
        return Write(connection, new { id, error = new { code = -32601, message = "CodexCraft read-only preview does not handle this request." } });
    }

    internal static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object>; }
    internal static object Field(Dictionary<string, object> map, string key)
    {
        object value;
        return map != null && map.TryGetValue(key, out value) ? value : null;
    }
    internal static string Text(Dictionary<string, object> map, string key) { return Field(map, key) as string; }

    sealed class Reply
    {
        readonly string thread;
        string final, fallback, status, error;
        internal string Id;
        internal bool Finished { get { return status != null; } }
        internal string Content { get { return final ?? fallback; } }

        internal Reply(string thread) { this.thread = thread; }

        internal void Accept(Dictionary<string, object> message)
        {
            var args = Map(Field(message, "params"));
            string method = Text(message, "method");
            if (Text(args, "threadId") != thread)
                return;
            if (method == "turn/completed")
            {
                var turn = Map(Field(args, "turn"));
                if (Id != null && Text(turn, "id") != Id)
                    return;
                status = Text(turn, "status") ?? "failed";
                error = Text(Map(Field(turn, "error")), "message");
            }
            else if (method == "item/completed" && (Id == null || Text(args, "turnId") == Id))
                AcceptItem(Map(Field(args, "item")));
        }

        internal void AcceptItem(Dictionary<string, object> item)
        {
            string text = Text(item, "text");
            if (Text(item, "type") != "agentMessage" || String.IsNullOrWhiteSpace(text))
                return;
            string phase = Text(item, "phase");
            if (phase == "final_answer")
                final = text;
            else if (phase == null || phase == "partial_answer")
                fallback = text;
        }

        internal string Result()
        {
            if (status != "completed")
                throw new InvalidOperationException(error ?? (status == "interrupted" ? "Codex was interrupted." : "The Codex turn failed."));
            string result = Content;
            if (String.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException("Codex did not return a final text reply.");
            return result;
        }
    }

    void Reset()
    {
        lock (lifetime)
        {
            var child = process;
            process = null;
            loadedThreadId = null;
            if (child == null)
                return;
            try { if (!child.HasExited) child.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { child.Dispose(); }
        }
    }

    public void Dispose()
    {
        lock (lifetime)
            disposed = true;
        Reset();
    }
}
