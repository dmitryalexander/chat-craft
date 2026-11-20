using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

// Native counterparts to Nidhogg's Ctrl-volume and absolute Shift-scrub gestures.
sealed partial class ChatCraftWindow
{
    readonly PlaybackGestureLayer gestureLayer = new PlaybackGestureLayer();
    bool volumeDragging, scrubbingReply, gestureMoved, volumeDirty;
    bool scrubWasOpen;
    double gestureStartY, gestureStartVolume;
    int scrubOriginalAnchor, scrubResumeDirection;
    int lastScrubTarget=-1;
    double? pendingScrubX;
    double nextScrubUpdate;
    string volumeSettingsPath;
    bool GestureActive { get { return volumeDragging || scrubbingReply; } }

    void InitializePlaybackGestures()
    {
        volumeSettingsPath=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","Audio-Settings.json"));
        LoadGestureVolume();
        layout.Children.Add(gestureLayer);
        Deactivated += (s,e) => { EndPlaybackGesture(true,false); gestureLayer.Clear(); };
        layout.LostMouseCapture += (s,e) =>
        {
            if(GestureActive && Mouse.Captured!=layout)EndPlaybackGesture(true,false);
        };
        MouseLeave += (s,e) => { if(!GestureActive)gestureLayer.Clear(); };
    }

    void LoadGestureVolume() { LoadAudioValues(); }

    void SaveGestureVolume() { if(volumeDirty)SaveAudioValues(); }

    internal static double DragVolume(double initial,double startY,double y)
    { return Math.Max(0,Math.Min(1,initial+(startY-y)/200)); }

    internal static int ScrubAnchor(double x,double width,int length)
    { return (int)Math.Round(Math.Max(0,Math.Min(1,x/Math.Max(1,width)))*Math.Max(0,length-1)); }

    bool HandleGestureKey(KeyEventArgs e)
    {
        if(e.Key==Key.Escape && GestureActive)
        {
            FinishPlaybackGesture(true);e.Handled=true;return true;
        }
        if(e.Key==Key.LeftShift||e.Key==Key.RightShift)
            UpdateGestureMarker(Mouse.GetPosition(layout));
        else if(GestureActive && e.Key!=Key.LeftCtrl && e.Key!=Key.RightCtrl)
            FinishPlaybackGesture(true);
        return false;
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if((volumeDragging&&(e.Key==Key.LeftCtrl||e.Key==Key.RightCtrl))||
            (scrubbingReply&&(e.Key==Key.LeftShift||e.Key==Key.RightShift)))
            FinishPlaybackGesture(false);
        if(e.Key==Key.LeftShift||e.Key==Key.RightShift)gestureLayer.Clear();
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if(helpOverlay!=null||colorPanel!=null||choosing||GestureActive)return;
        var modifiers=Keyboard.Modifiers;
        if(modifiers!=ModifierKeys.Control && modifiers!=ModifierKeys.Shift)return;
        Point p=e.GetPosition(layout);
        if(!InsideSimulation(p))return;
        if(modifiers==ModifierKeys.Control)
        {
            volumeDragging=true;gestureMoved=false;
            gestureStartY=p.Y;gestureStartVolume=morse.Volume;
        }
        else
        {
            if(!hasReply||WaitingForReply)return;
            BeginReplyScrub();
            SeekReplyScrub(p.X);
        }
        Mouse.Capture(layout,CaptureMode.SubTree);
        e.Handled=true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        Point p=e.GetPosition(layout);
        if(volumeDragging)
        {
            if(!gestureMoved&&Math.Abs(gestureStartY-p.Y)<3)return;
            gestureMoved=true;
            morse.Volume=DragVolume(gestureStartVolume,gestureStartY,p.Y);
            volumeDirty=true;SyncBackgroundSound();
            gestureLayer.Show(-1,"Volume "+Math.Round(morse.Volume*100)+"%");
            e.Handled=true;
        }
        else if(scrubbingReply)
        {
            pendingScrubX=p.X;
            UpdateGestureMarker(p);Wake();e.Handled=true;
        }
        else UpdateGestureMarker(p);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if(!GestureActive)return;
        if(scrubbingReply)SeekReplyScrub(e.GetPosition(layout).X);
        FinishPlaybackGesture(false);e.Handled=true;
    }

    void UpdateGestureMarker(Point p)
    {
        if(helpOverlay==null&&colorPanel==null&&!choosing&&hasReply&&!WaitingForReply&&Keyboard.Modifiers==ModifierKeys.Shift && (GestureActive||InsideSimulation(p)))
        {
            double fraction=Math.Max(0,Math.Min(1,p.X/Math.Max(1,layout.ActualWidth)));
            gestureLayer.Show(fraction,"Reply "+Math.Round(fraction*100)+"%");
        }
        else if(!GestureActive)gestureLayer.Clear();
    }

    internal void BeginReplyScrub()
    {
        scrubWasOpen=ReplyOpen;scrubOriginalAnchor=flow.Reader.TextAnchor;
        scrubResumeDirection=ReplyOpen?flow.Reader.PlaybackDirection:0;
        scrubbingReply=true;
        pendingScrubX=null;lastScrubTarget=-1;nextScrubUpdate=0;
        flow.Reader.PausePlayback();
        morse.ClearPending();scene.ClearReplySmoke();
        if(!ReplyOpen)ShowReply();
        returnAfterPlayback=false;
    }

    internal void SeekReplyScrub(double x)
    {
        if(!scrubbingReply)return;
        pendingScrubX=null;
        int target=ScrubAnchor(x,layout.ActualWidth,flow.TextLength);
        // Seek rebuilds the reader page; duplicate pointer targets do no work.
        if(target!=lastScrubTarget && target!=flow.Reader.TextAnchor)flow.Reader.Seek(target);
        lastScrubTarget=target;
        soundedAnchor=-1;
        replyOverlay.UpdateFrame();Wake();
        gestureLayer.Show(Math.Max(0,Math.Min(1,x/Math.Max(1,layout.ActualWidth))),
            "Reply "+Math.Round(Math.Max(0,Math.Min(1,x/Math.Max(1,layout.ActualWidth)))*100)+"%");
    }

    void TickReplyScrub()
    {
        double now=typingClock.Elapsed.TotalSeconds;
        if(!scrubbingReply||!pendingScrubX.HasValue||now<nextScrubUpdate)return;
        double x=pendingScrubX.Value;pendingScrubX=null;
        nextScrubUpdate=now+.033;
        SeekReplyScrub(x);
    }

    void FinishPlaybackGesture(bool cancel) { EndPlaybackGesture(cancel,!closed); }
    internal void EndPlaybackGesture(bool cancel,bool resumePlayback)
    {
        bool wasVolume=volumeDragging,wasScrub=scrubbingReply;
        if(wasScrub&&!cancel&&pendingScrubX.HasValue)SeekReplyScrub(pendingScrubX.Value);
        pendingScrubX=null;
        volumeDragging=scrubbingReply=false;
        SyncBackgroundSound();
        if(Mouse.Captured==layout)Mouse.Capture(null);
        gestureLayer.Clear();
        if(wasVolume)
        {
            if(cancel)morse.Volume=gestureStartVolume;
            SyncBackgroundSound();
            SaveGestureVolume();
        }
        if(wasScrub && !closed)
        {
            if(cancel)flow.Reader.Seek(scrubOriginalAnchor);
            if(cancel&&!scrubWasOpen)ShowDraft();
            else if(resumePlayback&&scrubResumeDirection!=0)
            { flow.Reader.StartPlayback(scrubResumeDirection);Wake(); }
        }
    }
}

sealed class PlaybackGestureLayer : FrameworkElement
{
    double fraction=-1;
    string label;
    internal PlaybackGestureLayer(){IsHitTestVisible=false;}
    internal void Show(double x,string text){fraction=x;label=text;InvalidateVisual();}
    internal void Clear(){label=null;InvalidateVisual();}
    protected override void OnRender(DrawingContext drawing)
    {
        if(label==null)return;
        if(fraction>=0)
            drawing.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(150,27,32,36)),1),
                new Point(fraction*ActualWidth,0),new Point(fraction*ActualWidth,ActualHeight));
        var text=new FormattedText(label,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),14,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var box=new Rect(Math.Max(8,ActualWidth-text.Width-28),14,text.Width+16,text.Height+10);
        drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(205,17,23,35)),null,box,7,7);
        drawing.DrawText(text,new Point(box.X+8,box.Y+5));
    }
}
