using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

sealed partial class ChatCraftWindow
{
    double windowScale = 1;
    double restoreWindowScale = .65;

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

    internal static double NextWindowScale(double current, int wheelDelta)
    {
        if (Double.IsNaN(current) || Double.IsInfinity(current)) current = 1;
        return Math.Max(.25, Math.Min(1, current + wheelDelta / 120.0 * .05));
    }

    internal static System.Drawing.Rectangle ScaledWindowBounds(System.Drawing.Rectangle screen, double scale)
    {
        scale = NextWindowScale(scale, 0);
        int width = Math.Max(1, (int)Math.Round(screen.Width * scale));
        int height = Math.Max(1, (int)Math.Round(screen.Height * scale));
        return new System.Drawing.Rectangle(screen.Left + (screen.Width - width) / 2,
            screen.Top + (screen.Height - height) / 2, width, height);
    }

    internal bool ApplyWindowScale(double value)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || closed) return false;
        double scale = NextWindowScale(value, 0);
        var bounds = ScaledWindowBounds(Forms.Screen.FromHandle(handle).Bounds, scale);
        WindowState = WindowState.Normal;
        MinWidth = MinHeight = 1;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        // Monitor bounds and SetWindowPos both use physical pixels, including on
        // monitors with negative coordinates or a different DPI from the primary.
        if (!SetWindowPos(handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x14)) return false;
        windowScale = scale;
        fullscreen = scale >= 1;
        if (!fullscreen) restoreWindowScale = scale;
        return true;
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (e.Handled || colorPanel != null) return;
        e.Handled = true;
        if (GestureActive || choosing || e.Delta == 0) return;
        ApplyWindowScale(NextWindowScale(windowScale, e.Delta));
    }
}
