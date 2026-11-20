using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;

sealed partial class ChatCraftWindow
{
    readonly Grid windowSurface = new Grid { Background = Brushes.Black };
    double simulationScale = 1;

    void InitializeSimulationViewport()
    {
        layout.HorizontalAlignment = HorizontalAlignment.Center;
        layout.VerticalAlignment = VerticalAlignment.Center;
        layout.ClipToBounds = true;
        windowSurface.Children.Add(layout);
        Content = windowSurface;
        windowSurface.SizeChanged += (s,e) => UpdateSimulationViewport();
    }

    void UpdateSimulationViewport()
    {
        if(windowSurface.ActualWidth <= 0 || windowSurface.ActualHeight <= 0)return;
        layout.Width = windowSurface.ActualWidth * simulationScale;
        layout.Height = windowSurface.ActualHeight * simulationScale;
        if(simulationScale < 1)
        {
            double radius=Math.Min(24,Math.Min(layout.Width,layout.Height)/2);
            var rounded=new RectangleGeometry(new Rect(0,0,layout.Width,layout.Height),radius,radius);
            rounded.Freeze();
            layout.Clip=rounded;
        }
        else layout.Clip=null;
        if(colorPanel!=null)colorPanel.MaxWidth=Math.Max(1,windowSurface.ActualWidth-36);
    }

    internal bool InsideSimulation(Point p)
    { return p.X>=0 && p.Y>=0 && p.X<layout.ActualWidth && p.Y<layout.ActualHeight && (layout.Clip==null || layout.Clip.FillContains(p)); }

    internal bool HandleSpaceShortcut(Key key, ModifierKeys modifiers, bool repeat)
    {
        if (key != Key.Space) return false;
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (!repeat) ReplayReply();
            return true;
        }
        // A held Shift must not eat the space between uppercase words, including
        // when typing resumes from a reply. Never pass this to the shared reader.
        if (modifiers == ModifierKeys.Shift && ReplyOpen) ShowDraft();
        return false;
    }

    internal static double NextSimulationScale(double current, int wheelDelta)
    {
        if (Double.IsNaN(current) || Double.IsInfinity(current)) current = 1;
        return Math.Max(.25, Math.Min(1, current + wheelDelta / 120.0 * .05));
    }

    internal bool ApplySimulationScale(double value)
    {
        if(closed)return false;
        simulationScale = NextSimulationScale(value, 0);
        UpdateSimulationViewport();
        return true;
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if(helpOverlay!=null){e.Handled=true;return;}
        if (e.Handled || colorPanel != null) return;
        e.Handled = true;
        if (GestureActive || choosing || e.Delta == 0) return;
        ApplySimulationScale(NextSimulationScale(simulationScale, e.Delta));
    }
}
