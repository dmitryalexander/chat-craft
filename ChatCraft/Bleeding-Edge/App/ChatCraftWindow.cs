using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("Chat Craft")]
[assembly: AssemblyProduct("Chat Craft")]
[assembly: AssemblyVersion("0.18.27.0")]

sealed partial class ChatCraftWindow : Window
{
    static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(2);
    static readonly TimeSpan ActiveReplyInterval = TimeSpan.FromMilliseconds(350);

    readonly CodexCraftClient client;
    readonly DesktopChatConnection desktop = new DesktopChatConnection();
    readonly CloudSmokeView scene = new CloudSmokeView();
    readonly TextBox draft = new TextBox { AcceptsReturn = true, Opacity = 0, IsHitTestVisible = false };
    readonly Grid layout = new Grid { Background = CloudSmokeView.Backdrop };
    readonly TextBlock chatTitle = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Button chatButton = new Button
    {
        HorizontalContentAlignment = HorizontalAlignment.Left, Focusable = false,
        Padding = new Thickness(16, 7, 16, 7),
        Foreground = Brushes.WhiteSmoke,
        Background = new SolidColorBrush(Color.FromRgb(26, 32, 43)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(53, 63, 76))
    };
    readonly DockPanel replyPane = new DockPanel { Visibility = Visibility.Collapsed };
    readonly TextBlock replyStatus = new TextBlock
    {
        Foreground = Brushes.Gainsboro, TextAlignment = TextAlignment.Center,
        Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed
    };
    readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    readonly DispatcherTimer watchTimer = new DispatcherTimer { Interval = WatchInterval };
    readonly Stopwatch time = new Stopwatch();
    readonly Stopwatch typingClock = Stopwatch.StartNew();
    readonly DialUpMorse morse = new DialUpMorse();
    WordFlowView flow;
    FlowSmokeOverlay replyOverlay;
    ChatColorPanel colorPanel;
    int colorResume;
    KineticReader colorReader;
    CancellationTokenSource desktopFollow;
    DesktopChatConnection.LiveTurn ownerTurn;
    CodexCraftClient.Chat watchedChat;
    Task watchRead;
    string playedTurnId;
    string desktopTurnId, observedTurnId, desktopSendStatus;
    string liveFlowTurnId, liveFlowText;
    DateTime nextLiveFlowUpdate;
    DateTime desktopReplyDeadline;
    bool thinking, choosing, closed, clearing, fullscreen;
    bool hasReply, returnAfterPlayback, fadingReply;
    bool desktopSendUnconfirmed;
    int soundedAnchor = -1;
    double nextFlowFrame;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string id);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [STAThread]
    static void Main()
    {
        SetCurrentProcessExplicitAppUserModelID("Dmitr.ChatCraft.Desktop");
        Forms.Application.EnableVisualStyles();
        var app = new Application();
        ErrorLog.Watch(app);
        string[] args = Environment.GetCommandLineArgs();
        int option = Array.IndexOf(args, "--codex-exe");
        var client = new CodexCraftClient(option > 0 && option + 1 < args.Length ? args[option + 1] : null,
            Environment.CurrentDirectory);
        app.Run(new ChatCraftWindow(client));
    }

    internal ChatCraftWindow(CodexCraftClient client)
    {
        this.client = client;
        Title = "Chat Craft";
        using (var icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("ChatCraft.ico"))
            Icon = BitmapFrame.Create(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Width = 1000;
        Height = 650;
        MinWidth = 280;
        MinHeight = 160;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        layout.Children.Add(draft);
        layout.Children.Add(scene);
        layout.Children.Add(replyPane);
        Content = layout;
        scene.Colors.Changed += ApplyReplyColor;
        UpdateChatTitle();
        draft.PreviewTextInput += (sender, args) =>
        {
            if (thinking || String.IsNullOrEmpty(args.Text))
                return;
            if (ReplyOpen && !String.IsNullOrWhiteSpace(args.Text))
                ShowDraft();
            double now = typingClock.Elapsed.TotalSeconds;
            morse.PlayTyped(args.Text, now);
            if (String.IsNullOrWhiteSpace(args.Text))
                return;
            string text = draft.Text;
            int start = draft.SelectionStart, end = start + draft.SelectionLength;
            int left = start, right = end;
            while (left > 0 && !Char.IsWhiteSpace(text[left - 1])) left--;
            while (right < text.Length && !Char.IsWhiteSpace(text[right])) right++;
            scene.DepositTypedVersion(text.Substring(left, start - left), args.Text,
                text.Substring(end, right - end));
        };
        draft.TextChanged += (sender, args) =>
        {
            if (clearing || thinking)
                return;
            if (!ReplyOpen && !WaitingForReply)
                scene.BeginDraftInput();
            desktopSendUnconfirmed = false;
            desktopSendStatus = null;
            ShowChatStatus();
            UpdateDraftWord();
        };
        draft.SelectionChanged += (sender, args) =>
        {
            if (!clearing && !thinking && !ReplyOpen && draft.Text.Length != 0)
                UpdateDraftWord();
        };
        timer.Tick += (sender, args) => Tick();
        watchTimer.Tick += (sender, args) => PollWatchedChat();
        Loaded += (sender, args) =>
        {
            ToggleFullscreen();
            draft.Focus();
            Dispatcher.BeginInvoke(new Action(ChooseChat), DispatcherPriority.ApplicationIdle);
        };
        StateChanged += (sender, args) => scene.SetSuspended(WindowState == WindowState.Minimized);
        InitializePlaybackGestures();
        MouseDown += (sender, args) => { if (!ReplyOpen && colorPanel==null) draft.Focus(); };
    }

    void ApplyReplyColor()
    {
        if(flow==null)return;
        Color ink=scene.Colors.Get(2,"text");
        flow.Reader.SingleLineInk=System.Drawing.Color.FromArgb(ink.R,ink.G,ink.B);
        if(replyOverlay!=null)replyOverlay.UpdateFrame();
    }

    void ToggleColors()
    {
        if(colorPanel!=null) { CloseColors(); return; }
        colorResume=ReplyOpen ? flow.Reader.PlaybackDirection : 0;
        colorReader=flow==null ? null : flow.Reader;
        if(colorResume!=0)colorReader.PausePlayback();
        colorPanel=new ChatColorPanel(scene.Colors,scene.Phase,
            phase=>scene.PreviewColors(phase),CloseColors);
        Panel.SetZIndex(colorPanel,100);
        layout.Children.Add(colorPanel);
        KeyboardNavigation.SetTabNavigation(colorPanel,KeyboardNavigationMode.Cycle);
        layout.UpdateLayout();
        colorPanel.FocusEditor();
    }

    void CloseColors()
    {
        if(colorPanel==null || !colorPanel.Save())return;
        layout.Children.Remove(colorPanel);
        colorPanel=null;
        scene.PreviewColors(null);
        ApplyReplyColor();
        draft.Focus();
        if(colorResume!=0 && ReplyOpen && flow.Reader==colorReader)
            colorReader.StartPlayback(colorResume);
        colorResume=0;colorReader=null;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        HandleFullscreenPreviewKeyDown(this, e);
        if (e.Handled)
            return;
        if (HandleGestureKey(e)) return;
        if(e.Key==Key.Capital)
        {
            e.Handled=true;
            if(!e.IsRepeat)ToggleColors();
            return;
        }
        if(colorPanel!=null)
        {
            if(e.Key==Key.Escape) { e.Handled=true;CloseColors(); }
            return;
        }
        ModifierKeys modifiers = Keyboard.Modifiers;
        if (e.Key == Key.Space && modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            if (!e.IsRepeat)
                ReplayReply();
            return;
        }
        if (ReplyOpen)
        {
            var key = (Forms.Keys)KeyInterop.VirtualKeyFromKey(e.Key);
            if ((modifiers & ModifierKeys.Control) != 0) key |= Forms.Keys.Control;
            if ((modifiers & ModifierKeys.Shift) != 0) key |= Forms.Keys.Shift;
            if ((modifiers & ModifierKeys.Alt) != 0) key |= Forms.Keys.Alt;
            e.Handled = HandleReplyKey(key, e.IsRepeat);
            return;
        }
        if (e.Key == Key.Tab && modifiers == ModifierKeys.Control && hasReply && !WaitingForReply && !choosing)
        {
            e.Handled = true;
            if (!e.IsRepeat)
                ShowReply();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (!e.IsRepeat)
                Close();
        }
        else if (e.Key == Key.Enter && modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            if (!e.IsRepeat)
                SendPrompt();
        }
        else if (e.Key == Key.O && modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (!e.IsRepeat)
                ChooseChat();
        }
        else if (!draft.IsKeyboardFocused && !thinking)
            draft.Focus();

        if (e.Key == Key.Space && !e.Handled && modifiers == ModifierKeys.None && !thinking)
            scene.CommitWord();
    }

    void UpdateDraftWord()
    {
        string text = draft.Text;
        int left = Math.Max(0, Math.Min(draft.CaretIndex, text.Length)), right = left;
        while (left > 0 && !Char.IsWhiteSpace(text[left - 1])) left--;
        while (right < text.Length && !Char.IsWhiteSpace(text[right])) right++;
        scene.ShowWord(text.Substring(left, right - left));
        Wake();
    }

    async void SendPrompt()
    {
        if (thinking || choosing || closed)
            return;
        if (WaitingForReply || desktopSendUnconfirmed)
        {
            ShowChatStatus();
            return;
        }
        var watched = watchedChat;
        if (watched == null && client.SelectedChat == null)
        {
            ChooseChat();
            return;
        }
        string prompt = draft.Text;
        if (String.IsNullOrWhiteSpace(prompt))
            return;
        morse.EndDraft();
        thinking = true;
        draft.IsReadOnly = true;
        scene.BeginThinking();
        ShowStatus("");
        Wake();
        try
        {
            if (watched == null)
            {
                string reply = await client.SendAsync(prompt);
                if (!closed)
                {
                    ClearSentDraft();
                    BeginReply(reply);
                }
            }
            else
            {
                if (watchRead != null)
                    await watchRead;
                if (closed)
                    return;
                string turnId = await desktop.SendAsync(watched.Id, prompt);
                if (!closed)
                {
                    desktopTurnId = turnId;
                    desktopReplyDeadline = DateTime.UtcNow.AddMinutes(3);
                    watchTimer.Interval = ActiveReplyInterval;
                    desktopSendStatus = null;
                    ClearSentDraft();
                    ShowChatStatus();
                }
            }
        }
        catch (Exception error)
        {
            if (!closed)
            {
                scene.ResumePigmentFade();
                UpdateDraftWord();
                if (watched == null)
                    ShowStatus(error.Message);
                else
                {
                    desktopSendUnconfirmed = error is DesktopChatConnection.UnconfirmedSendException;
                    desktopSendStatus = error.Message;
                    ShowStatus(desktopSendStatus);
                }
            }
        }
        finally
        {
            thinking = false;
            if (!closed)
            {
                draft.IsReadOnly = false;
                if (desktopTurnId != null && ownerTurn != null && ownerTurn.Id == desktopTurnId)
                    AcceptLiveTurn(ownerTurn);
                Wake();
                PollWatchedChat();
            }
        }
    }

    void ClearSentDraft()
    {
        clearing = true;
        draft.IsUndoEnabled = false;
        draft.Clear();
        draft.IsUndoEnabled = true;
        clearing = false;
    }

    internal void BeginReply(string reply)
    {
        thinking = false;
        liveFlowTurnId = liveFlowText = null;
        OpenReplyText(reply);
    }

    void OpenReplyText(string reply)
    {
        if (flow == null)
        {
            flow = new WordFlowView(new WindowInteropHelper(this).Handle, null, FlowProfileDirectory, false);
            flow.Reader.SetFontFamily(ChatTypography.Family);
            flow.Reader.ReadSlowPlaybackModifier = () => IsActive && (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            flow.Reader.SetTuning("circleVisible", 0);
            flow.WordClicked += flow.FocusWord;
            flow.Reader.Navigated += anchor => SoundReplyLetter();
            flow.Reader.PlaybackChanged += SoundReplyLetter;
            replyOverlay = new FlowSmokeOverlay(flow, Wake, scene);
            ApplyReplyColor();
            replyPane.Children.Add(replyOverlay);
        }
        if (hasReply)
            flow.Leave();
        replyOverlay.ClearFrame();
        hasReply = true;
        soundedAnchor = -1;
        flow.Open(reply, 0, takeFocus: false);
        ShowReply(autoplay: true);
        Wake();
    }

    bool ReplyOpen { get { return replyPane.Visibility == Visibility.Visible; } }
    bool WaitingForReply { get { return thinking || desktopTurnId != null || observedTurnId != null; } }

    void ShowReply(bool autoplay = false)
    {
        ResetReplyFade();
        scene.BeginReply();
        flow.Reader.SmokeFade = 0;
        draft.Focus();
        scene.ShowError("");
        replyPane.Visibility = Visibility.Visible;
        layout.UpdateLayout();
        replyOverlay.ShowText();
        replyOverlay.UpdateFrame();
        replyPane.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(700)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        if (autoplay)
        {
            if(colorPanel==null)flow.Reader.StartPlayback(1);
            else { colorResume=1;colorReader=flow.Reader; }
        }
        Wake();
    }

    void ShowDraft()
    {
        ResetReplyFade();
        flow.Reader.PausePlayback();
        replyPane.Visibility = Visibility.Collapsed;
        scene.ShowError(replyStatus.Text);
        if (WaitingForReply)
            scene.BeginThinking();
        else
            scene.KeepReplyPigment();
        draft.Focus();
        UpdateDraftWord();
    }

    void ReplayReply()
    {
        if (!hasReply || WaitingForReply || choosing)
            return;
        flow.Reader.PausePlayback();
        flow.Reader.Seek(0);
        soundedAnchor = -1;
        ShowReply(autoplay: true);
    }

    void ResetReplyFade()
    {
        replyPane.BeginAnimation(OpacityProperty, null);
        replyPane.Opacity = 1;
        fadingReply = returnAfterPlayback = false;
    }

    void FadeReply()
    {
        fadingReply = true;
        scene.KeepReplyPigment();
        replyOverlay.HideText();
        var fade = new DoubleAnimation(replyPane.Opacity, 0, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        fade.Completed += (sender, args) =>
        {
            if (!closed && fadingReply)
            {
                ShowDraft();
                replyOverlay.ClearFrame();
            }
        };
        replyPane.BeginAnimation(OpacityProperty, fade);
    }

    void SoundReplyLetter()
    {
        var reader = flow.Reader;
        if (!ReplyOpen || reader.PlaybackDirection == 0)
            return;
        returnAfterPlayback = true;
        if (soundedAnchor == reader.TextAnchor)
            return;
        soundedAnchor = reader.TextAnchor;
        scene.TypeReplyLetter(reader.FocusedWordStart, reader.FocusedWordPrefix, reader.FocusedGrapheme);
        morse.PlayFocused(reader.FocusedWordText, reader.TextAnchor-reader.FocusedWordStart,
            reader.FocusedGrapheme, 1 / reader.EffectiveLettersPerSecond);
    }

    bool HandleReplyKey(Forms.Keys key, bool repeat)
    {
        switch (key)
        {
            case Forms.Keys.Escape:
                if (!repeat) Close();
                return true;
            case Forms.Keys.Control | Forms.Keys.O:
                if (!repeat) ChooseChat();
                return true;
            case Forms.Keys.Control | Forms.Keys.Tab:
            case Forms.Keys.Enter:
                if (!repeat) ShowDraft();
                return true;
            case Forms.Keys.Control | Forms.Keys.V:
                if (!repeat)
                {
                    ShowDraft();
                    draft.Paste();
                }
                return true;
            case Forms.Keys.Oemtilde:
            case Forms.Keys.Shift | Forms.Keys.Oemtilde:
                if (!repeat) ToggleFullscreen();
                return true;
        }
        if (repeat && (key & Forms.Keys.KeyCode) == Forms.Keys.Space)
            return true;
        bool handled = flow.HandleKey(key);
        if (handled)
            Wake();
        return handled;
    }

    void Wake()
    {
        if (timer.IsEnabled || closed)
            return;
        time.Restart();
        nextFlowFrame = 0;
        timer.Start();
    }

    void Tick()
    {
        TickReplyScrub();
        double now = time.Elapsed.TotalSeconds;
        bool waiting = WaitingForReply;
        bool flowing = false;
        if (ReplyOpen && replyOverlay != null)
        {
            scene.FlushReplySmoke();
            var reader = flow.Reader;
            flowing = reader.Animating;
            if (now >= nextFlowFrame)
            {
                nextFlowFrame = now + .016;
                reader.SmokeFade = fadingReply ? 1 : 0;
                replyOverlay.UpdateFrame();
            }
            if (!scrubbingReply && returnAfterPlayback && !fadingReply && !waiting && !choosing && !scene.ReplySmokePending &&
                reader.PlaybackDirection == 0 && reader.AtBufferedEnd)
                FadeReply();
        }
        if (!flowing && !fadingReply && !scene.ReplySmokePending && !scrubbingReply)
            timer.Stop();
    }

    void ShowChatStatus()
    {
        if (watchedChat == null || WaitingForReply)
            ShowStatus("");
        else
            ShowStatus(desktopSendUnconfirmed ? desktopSendStatus ?? "" : "");
    }

    CodexCraftClient.Chat CurrentChat { get { return watchedChat ?? client.SelectedChat; } }

    void UpdateChatTitle()
    {
        var chat = CurrentChat;
        chatTitle.Text = chat == null ? "Open saved chat" : chat.Title;
        chatButton.ToolTip = chat == null ? "Open saved chat (Ctrl+O)" :
            chat + "\nSwitch chat (Ctrl+O)";
        System.Windows.Automation.AutomationProperties.SetName(chatButton,
            chat == null ? "Open saved chat" : "Switch chat. Current chat: " + chat.Title);
    }

    void ShowStatus(string text)
    {
        if (replyStatus.Text == text)
            return;
        scene.ShowError(ReplyOpen ? "" : text);
        replyStatus.Text = text;
        replyStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    void SetWatchedChat(CodexCraftClient.Chat chat)
    {
        if (desktopFollow != null)
            desktopFollow.Cancel();
        desktopFollow = null;
        ownerTurn = null;
        watchedChat = chat;
        playedTurnId = null;
        FinishDesktopWait();
        scene.ResumePigmentFade();
        desktopSendStatus = null;
        desktopSendUnconfirmed = false;
        if (chat == null)
            watchTimer.Stop();
        else
        {
            watchTimer.Start();
            desktopFollow = new CancellationTokenSource();
            FollowDesktop(chat, desktopFollow);
        }
    }

    async void FollowDesktop(CodexCraftClient.Chat chat, CancellationTokenSource lifetime)
    {
        try
        {
            await desktop.FollowAsync(chat.Id, turn => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!closed && watchedChat == chat && desktopFollow == lifetime)
                    AcceptLiveTurn(turn);
            }), DispatcherPriority.Background), lifetime.Token);
        }
        catch (Exception error)
        {
            if (!lifetime.IsCancellationRequested)
                Trace.WriteLine("Chat Craft live reply feed: " + error.GetType().Name);
            // Saved-turn polling remains active.
        }
        finally
        {
            if (desktopFollow == lifetime)
                desktopFollow = null;
            lifetime.Dispose();
        }
    }

    void TrackActiveTurn(string id)
    {
        if (observedTurnId != id)
        {
            if (desktopTurnId == null)
            {
                if (ReplyOpen)
                    ShowDraft();
                scene.BeginThinking();
                desktopReplyDeadline = DateTime.UtcNow.AddMinutes(3);
                Wake();
            }
        }
        observedTurnId = id;
        if (watchTimer.Interval != ActiveReplyInterval)
            watchTimer.Interval = ActiveReplyInterval;
        ShowChatStatus();
    }

    void AcceptLiveTurn(DesktopChatConnection.LiveTurn turn)
    {
        if (turn.Id == playedTurnId ||
            (desktopTurnId != null && turn.Id != desktopTurnId))
            return;
        ownerTurn = turn;
        if (thinking || choosing)
            return;
        if (turn.Status == "inProgress")
        {
            TrackActiveTurn(turn.Id);
            if (String.IsNullOrWhiteSpace(turn.Answer) || draft.Text.Length != 0 ||
                DateTime.UtcNow < nextLiveFlowUpdate)
                return;
            int end = turn.Answer.Length - 1;
            while (end >= 0 && !Char.IsWhiteSpace(turn.Answer[end]))
                end--;
            if (end >= 0)
                PlayLiveText(turn.Id, turn.Answer.Substring(0, end + 1).TrimEnd());
            return;
        }
        if (turn.Id != observedTurnId && turn.Id != desktopTurnId)
            return;
        if ((turn.Status == "completed" && !String.IsNullOrWhiteSpace(turn.Answer)) ||
            turn.Status == "interrupted" || turn.Status == "failed" || turn.Status == "cancelled")
            CompleteWatchedTurn(turn.Id, turn.Status, turn.Answer);
        else
            PollWatchedChat();
    }

    void PlayLiveText(string id, string text)
    {
        if (String.IsNullOrWhiteSpace(text) || text == liveFlowText)
            return;
        nextLiveFlowUpdate = DateTime.UtcNow.AddMilliseconds(125);
        if (liveFlowTurnId != id)
        {
            liveFlowTurnId = id;
            liveFlowText = text;
            OpenReplyText(text);
            return;
        }
        if (!ReplyOpen)
            return;
        var reader = flow.Reader;
        bool resume = reader.PlaybackDirection == 0 && reader.AtBufferedEnd;
        liveFlowText = text;
        flow.UpdateReplyText(text);
        if (resume)
            reader.StartPlayback(1);
        Wake();
    }

    void FinishDesktopWait()
    {
        desktopTurnId = observedTurnId = null;
        watchTimer.Interval = WatchInterval;
    }

    void PollWatchedChat()
    {
        if (!closed && !choosing && !thinking && watchedChat != null && (watchRead == null || watchRead.IsCompleted))
            watchRead = RefreshWatchedChat(watchedChat);
    }

    async Task RefreshWatchedChat(CodexCraftClient.Chat chat)
    {
        try
        {
            var expectedTurn = desktopTurnId ?? observedTurnId;
            var reply = await client.ReadLatestReplyAsync(chat, expectedTurn, observeLatestTurn: true);
            if (closed || choosing || thinking || watchedChat != chat ||
                expectedTurn != (desktopTurnId ?? observedTurnId))
                return;
            if (reply != null && reply.IsActive)
            {
                TrackActiveTurn(reply.TurnId);
                return;
            }
            if (reply == null)
            {
                if (expectedTurn != null && DateTime.UtcNow >= desktopReplyDeadline)
                    watchTimer.Interval = WatchInterval;
                return;
            }
            if (reply.Status == "interrupted" && expectedTurn != null)
            {
                // A separate history reader can see an unfinished journal as interrupted.
                var live = ownerTurn != null && ownerTurn.Id == reply.TurnId ? ownerTurn : null;
                if (live != null && live.Status == "completed")
                    return; // Wait for the saved text if the owner completed without publishing its answer.
                if ((live != null && live.Status == "inProgress" && desktopFollow != null) ||
                    DateTime.UtcNow < desktopReplyDeadline)
                    return;
            }
            CompleteWatchedTurn(reply.TurnId, reply.Status, reply.Text);
        }
        catch (Exception error)
        {
            if (!closed && !choosing && watchedChat == chat)
            {
                if (desktopTurnId != null && DateTime.UtcNow >= desktopReplyDeadline)
                    watchTimer.Interval = WatchInterval;
                ShowStatus((desktopSendStatus ?? "Reply check failed") + " · " + error.Message);
            }
        }
    }

    void CompleteWatchedTurn(string id, string status, string text)
    {
        if (WaitingForReply)
        {
            FinishDesktopWait();
            if (status != "completed")
            {
                scene.ResumePigmentFade();
                desktopSendStatus = "Desktop turn ended · " + status;
                if (liveFlowTurnId == id && ReplyOpen)
                    ShowDraft();
                liveFlowTurnId = liveFlowText = null;
            }
            else
                desktopSendStatus = String.IsNullOrWhiteSpace(text) ? "Desktop turn completed without a text reply" : null;
        }
        ShowChatStatus();
        if (status != "completed" || String.IsNullOrWhiteSpace(text) || id == playedTurnId || draft.Text.Length != 0)
        {
            if (!ReplyOpen || String.IsNullOrWhiteSpace(text))
                scene.ResumePigmentFade();
            return;
        }
        playedTurnId = id;
        if (liveFlowTurnId == id && ReplyOpen)
        {
            PlayLiveText(id, text);
            liveFlowTurnId = liveFlowText = null;
        }
        else
            BeginReply(text);
        Wake();
    }

    async void ChooseChat()
    {
        if (thinking || choosing || closed)
            return;
        choosing = true;
        int direction = ReplyOpen ? flow.Reader.PlaybackDirection : 0;
        if (direction != 0)
            flow.Reader.PausePlayback();
        try
        {
            if (watchRead != null)
                await watchRead;
            if (closed)
                return;
            var picker = new CodexChatPicker(this, client, allowNewChat: false, currentChat: CurrentChat);
            picker.PreviewKeyDown += HandleFullscreenPreviewKeyDown;
            bool accepted = picker.ShowDialog() == true;
            if (picker.PendingLoad != null)
                await picker.PendingLoad;
            if (closed || !accepted)
                return;
            direction = 0;
            if (picker.Chosen != null)
                await OpenSavedChatAsync(picker.Chosen);
        }
        catch (Exception error)
        {
            if (!closed)
                ShowStatus(error.Message);
        }
        finally
        {
            choosing = false;
            if (!closed)
            {
                draft.Focus();
                if (ReplyOpen && direction != 0)
                    flow.Reader.StartPlayback(direction);
                Wake();
                PollWatchedChat();
            }
        }
    }

    async Task OpenSavedChatAsync(CodexCraftClient.Chat chat)
    {
        bool watch = false;
        try
        {
            await client.SelectChatAsync(chat);
        }
        catch (InvalidOperationException error)
        {
            if (error.Message.IndexOf("thread " + chat.Id + " already has an active writer",
                StringComparison.Ordinal) < 0)
                throw;
            watch = true;
        }
        if (closed)
            return;

        SetWatchedChat(watch ? chat : null);
        UpdateChatTitle();
        ResetReply();
        if (!watch)
        {
            var latest = await client.ReadLatestReplyAsync(chat);
            if (!closed && latest != null)
                BeginReply(latest.Text);
        }
    }

    void ResetReply()
    {
        liveFlowTurnId = liveFlowText = null;
        nextLiveFlowUpdate = DateTime.MinValue;
        if (hasReply)
        {
            if (ReplyOpen)
                ShowDraft();
            flow.Leave();
            hasReply = false;
        }
        ShowChatStatus();
        UpdateDraftWord();
    }

    void HandleFullscreenPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Oem3 || (Keyboard.Modifiers & ~ModifierKeys.Shift) != ModifierKeys.None)
            return;
        e.Handled = true;
        if (!e.IsRepeat)
            ToggleFullscreen();
    }

    void ToggleFullscreen()
    {
        if (fullscreen)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            Width = 1000;
            Height = 650;
            fullscreen = false;
            return;
        }
        IntPtr window = new WindowInteropHelper(this).Handle;
        if (window == IntPtr.Zero)
            return;
        var bounds = Forms.Screen.FromHandle(window).Bounds;
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        fullscreen = SetWindowPos(window, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x14);
    }

    protected override void OnClosed(EventArgs e)
    {
        if(colorPanel!=null)colorPanel.Save();
        closed = true;
        FinishPlaybackGesture(true);
        SaveGestureVolume();
        timer.Stop();
        watchTimer.Stop();
        if (desktopFollow != null)
            desktopFollow.Cancel();
        morse.Dispose();
        if (flow != null)
            flow.Dispose();
        scene.Dispose();
        client.Dispose();
        base.OnClosed(e);
    }

    static string FlowProfileDirectory
    {
        get
        {
            string appDirectory = Path.GetDirectoryName(typeof(ChatCraftWindow).Assembly.Location);
            return Path.GetFullPath(Path.Combine(appDirectory, "../../../WordCraft"));
        }
    }
}
