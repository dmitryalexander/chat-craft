using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

sealed class CodexChatPicker : Window
{
    readonly CodexCraftClient client;
    readonly CodexCraftClient.Chat currentChat;
    readonly bool allowNewChat;
    readonly TextBox search = new TextBox { MinWidth = 220, Margin = new Thickness(0, 0, 8, 0) };
    readonly ListBox chats = new ListBox { Margin = new Thickness(0, 10, 0, 10) };
    readonly TextBlock status = new TextBlock { Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    readonly Button find = MakeButton("Search"), more = MakeButton("Load more"),
        use = MakeButton("Open chat"), fresh = MakeButton("New chat");
    string cursor, query;
    bool closed, loading;

    internal CodexCraftClient.Chat Chosen { get; private set; }
    internal bool WantsNew { get; private set; }
    internal Task PendingLoad { get; private set; }

    internal CodexChatPicker(Window owner, CodexCraftClient client, bool allowNewChat = true,
        CodexCraftClient.Chat currentChat = null)
    {
        this.client = client;
        this.allowNewChat = allowNewChat;
        this.currentChat = currentChat ?? client.SelectedChat;
        Owner = owner;
        Icon = owner.Icon;
        Title = allowNewChat ? "CodexCraft chats" : "Open saved chat";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 660;
        Height = 510;
        MinWidth = 420;
        MinHeight = 320;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(24, 26, 31));
        Foreground = Brushes.WhiteSmoke;
        var layout = new DockPanel { Margin = new Thickness(16) };
        Content = layout;
        var searchRow = new DockPanel();
        DockPanel.SetDock(searchRow, Dock.Top);
        DockPanel.SetDock(find, Dock.Right);
        searchRow.Children.Add(find);
        searchRow.Children.Add(search);
        System.Windows.Automation.AutomationProperties.SetName(search, "Search chat titles");
        layout.Children.Add(searchRow);

        var footer = new StackPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        footer.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        footer.Children.Add(buttons);
        buttons.Children.Add(use);
        if (allowNewChat)
        {
            buttons.Children.Add(fresh);
            fresh.Click += (sender, args) => { WantsNew = true; DialogResult = true; };
        }
        buttons.Children.Add(more);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 5, 12, 5) });
        layout.Children.Add(chats);
        chats.SelectionChanged += (sender, args) => UpdateButtons();
        chats.MouseDoubleClick += (sender, args) =>
        {
            DependencyObject source = args.OriginalSource as DependencyObject;
            if (source != null && ItemsControl.ContainerFromElement(chats, source) is ListBoxItem)
                Choose();
        };
        chats.PreviewKeyDown += (sender, args) =>
        {
            if (args.Key == Key.Enter) { args.Handled = true; Choose(); }
        };
        search.PreviewKeyDown += (sender, args) =>
        {
            if (args.Key == Key.Enter) { args.Handled = true; BeginLoad(true); }
        };
        find.Click += (sender, args) => BeginLoad(true);
        more.Click += (sender, args) => BeginLoad(false);
        use.Click += (sender, args) => Choose();
        Loaded += (sender, args) => { search.Focus(); BeginLoad(true); };
        Closed += (sender, args) => closed = true;
        UpdateButtons();
    }

    static Button MakeButton(string label)
    {
        return new Button { Content = label, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 5, 12, 5) };
    }

    void Choose()
    {
        var row = chats.SelectedItem as ListBoxItem;
        if (loading || row == null)
            return;
        Chosen = row.Tag as CodexCraftClient.Chat;
        DialogResult = true;
    }

    void BeginLoad(bool replace)
    {
        if (!loading)
            PendingLoad = LoadPage(replace);
    }

    async Task LoadPage(bool replace)
    {
        loading = true;
        if (replace)
        {
            query = search.Text;
            cursor = null;
            chats.Items.Clear();
        }
        status.Text = "Loading saved chats…";
        UpdateButtons();
        try
        {
            var page = await client.ListChatsAsync(cursor, query);
            if (closed)
                return;
            cursor = page.NextCursor;
            foreach (var chat in page.Chats)
            {
                var row = new ListBoxItem { Tag = chat, Padding = new Thickness(8, 5, 8, 5) };
                row.Content = new TextBlock { Text = chat.ToString(), TextTrimming = TextTrimming.CharacterEllipsis };
                chats.Items.Add(row);
                if (currentChat != null && chat.Id == currentChat.Id)
                    chats.SelectedItem = row;
            }
            status.Text = chats.Items.Count == 0 ? "No saved chats found." :
                allowNewChat ? "Choose a chat or start a new one." : "Choose a saved chat.";
        }
        catch (Exception error)
        {
            if (!closed)
                status.Text = error.Message;
        }
        finally
        {
            loading = false;
            if (!closed)
                UpdateButtons();
        }
    }

    void UpdateButtons()
    {
        find.IsEnabled = !loading;
        more.IsEnabled = !loading && cursor != null;
        use.IsEnabled = !loading && chats.SelectedItem != null;
    }
}
