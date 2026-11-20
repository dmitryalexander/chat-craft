using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

sealed partial class ChatCraftWindow
{
    ChatHelpOverlay helpOverlay;
    KineticReader helpReader;
    int helpResume;
    bool helpClosing;

    internal void ToggleHelp()
    {
        if(helpOverlay!=null){CloseHelp();return;}
        FinishPlaybackGesture(true);
        gestureLayer.Clear();
        helpReader=flow==null?null:flow.Reader;
        helpResume=ReplyOpen?helpReader.PlaybackDirection:0;
        if(helpResume!=0)helpReader.PausePlayback();
        morse.ClearPending();
        helpOverlay=new ChatHelpOverlay(CloseHelp);
        Panel.SetZIndex(helpOverlay,200);
        windowSurface.Children.Add(helpOverlay);
        helpOverlay.Focus();
        helpOverlay.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(200)));
        SyncBackgroundSound();
    }

    internal void CloseHelp()
    {
        if(helpOverlay==null || helpClosing)return;
        helpClosing=true;
        var overlay=helpOverlay;
        var fade=new DoubleAnimation(overlay.Opacity,0,TimeSpan.FromMilliseconds(160));
        fade.Completed+=(s,e)=>FinishHelpClose(overlay);
        overlay.BeginAnimation(OpacityProperty,fade);
    }

    internal void FinishHelpClose(ChatHelpOverlay overlay)
    {
        if(helpOverlay!=overlay)return;
        windowSurface.Children.Remove(overlay);
        helpOverlay=null;helpClosing=false;
        if(!closed)
        {
            if(colorPanel!=null)colorPanel.FocusEditor();else draft.Focus();
            if(helpResume!=0 && colorPanel==null && ReplyOpen && flow.Reader==helpReader)
                helpReader.StartPlayback(helpResume);
        }
        helpReader=null;helpResume=0;
        SyncBackgroundSound();
    }
}

// Vector keycaps, never a screenshot or a second window. The help always uses
// the unscaled window surface; the user's simulation stays visible behind it.
sealed class ChatHelpOverlay : Grid
{
    static readonly Brush Ink=new SolidColorBrush(Color.FromRgb(235,240,247));
    static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(157,173,192));
    internal ChatHelpOverlay(Action close)
    {
        Focusable=true;
        Background=new SolidColorBrush(Color.FromArgb(245,0,0,0));
        MouseDown+=(s,e)=>e.Handled=true;
        var sheet=new StackPanel {Width=1000};
        sheet.Children.Add(new TextBlock {Text="Keyboard controls",FontSize=34,FontWeight=FontWeights.SemiBold,Foreground=Ink});
        sheet.Children.Add(new TextBlock {Text="Shortcuts & mouse gestures",FontSize=15,Foreground=Muted,Margin=new Thickness(0,9,0,34)});
        var columns=new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(56)});
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left=new StackPanel();var right=new StackPanel();Grid.SetColumn(right,2);
        columns.Children.Add(left);columns.Children.Add(right);sheet.Children.Add(columns);
        Section(left,"WRITE & CHAT");
        Row(left,"Type your message",new[]{"A","…","Z"});
        Row(left,"Send message",new[]{"Enter"});
        Row(left,"Choose a saved chat",new[]{"Ctrl","O"});
        Row(left,"Sound, quality & colors",new[]{"Caps Lock"});
        Section(left,"VIEW",20);
        Row(left,"Simulation size · 25–100%",new[]{"Wheel ↑↓"});
        Row(left,"Window fullscreen",new[]{"`"});
        Section(right,"READ & LISTEN");
        Row(right,"Pause / resume reply",new[]{"Space"});
        Row(right,"Hold to read more slowly",new[]{"Shift"});
        Row(right,"Replay from the beginning",new[]{"Ctrl","Shift","Space"});
        Row(right,"Last reply · start paused",new[]{"Ctrl","Tab"});
        Row(right,"Seek through the reply",new[]{"Shift","Drag ↔"});
        Row(right,"Sound volume",new[]{"Ctrl","Drag ↕"});
        var footer=new Grid {Margin=new Thickness(0,34,0,0)};
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        footer.Children.Add(new TextBlock {Text="Mouse gestures use the left button. Settings stay full size.",FontSize=13,Foreground=Muted,VerticalAlignment=VerticalAlignment.Center});
        var dismiss=new Button {Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),Cursor=Cursors.Hand};
        System.Windows.Automation.AutomationProperties.SetName(dismiss,"Close keyboard controls");
        var dismissContent=new StackPanel {Orientation=Orientation.Horizontal};
        dismissContent.Children.Add(Keycap("F1"));
        dismissContent.Children.Add(new TextBlock {Text=" or ",Foreground=Muted,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(7,0,7,0)});
        dismissContent.Children.Add(Keycap("Esc"));
        dismissContent.Children.Add(new TextBlock {Text="  to close",Foreground=Muted,FontSize=14,VerticalAlignment=VerticalAlignment.Center});
        dismiss.Content=dismissContent;dismiss.Click+=(s,e)=>close();Grid.SetColumn(dismiss,1);footer.Children.Add(dismiss);sheet.Children.Add(footer);
        var fit=new Viewbox {Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Margin=new Thickness(44),Child=sheet};
        Children.Add(fit);
        KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.Cycle);
    }

    static void Section(Panel parent,string name,double top=0)
    {
        parent.Children.Add(new TextBlock {Text=name,FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=Muted,Margin=new Thickness(0,top,0,13)});
    }

    static void Row(Panel parent,string description,string[] keys)
    {
        var row=new Grid {Margin=new Thickness(0,0,0,13),MinHeight=44};
        row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var caps=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        for(int i=0;i<keys.Length;i++)
        {
            if(i>0)caps.Children.Add(new TextBlock {Text=keys[i]=="…"||keys[i-1]=="…"?" ":"+",Foreground=Muted,FontSize=13,Margin=new Thickness(6,0,6,0),VerticalAlignment=VerticalAlignment.Center});
            caps.Children.Add(keys[i]=="…"?(UIElement)new TextBlock {Text="…",Foreground=Muted,VerticalAlignment=VerticalAlignment.Center}:Keycap(keys[i]));
        }
        row.Children.Add(caps);
        var text=new TextBlock {Text=description,Foreground=Ink,FontSize=15,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(14,0,0,0)};
        Grid.SetColumn(text,1);row.Children.Add(text);parent.Children.Add(row);
    }

    static Border Keycap(string text)
    {
        double width=text=="Space"?76:text=="Caps Lock"?104:text.StartsWith("Wheel")?104:text.StartsWith("Drag")?87:text=="Shift"?62:text=="Enter"?66:text=="Ctrl"?55:43;
        var outer=new Border {Width=width,Height=43,CornerRadius=new CornerRadius(7),Margin=new Thickness(0,0,0,2),Background=new SolidColorBrush(Color.FromRgb(11,16,23)),Padding=new Thickness(0,0,0,3)};
        var face=new Border {CornerRadius=new CornerRadius(6),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromRgb(87,106,128)),Background=new SolidColorBrush(Color.FromRgb(32,43,58))};
        face.Child=new TextBlock {Text=text,Foreground=Ink,FontFamily=new FontFamily("Segoe UI"),FontWeight=FontWeights.Medium,FontSize=14,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        outer.Child=face;return outer;
    }
}
