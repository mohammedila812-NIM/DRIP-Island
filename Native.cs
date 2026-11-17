using System;
using System.Runtime.InteropServices;

namespace IslandWin;

/// <summary>Tiny Win32 surface: global cursor position and click-through toggling.</summary>
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong(IntPtr h, int index, int value);

    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x00000020;
    const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Hide from Alt+Tab and start click-through.</summary>
    public static void Prepare(IntPtr h)
    {
        int s = GetWindowLong(h, GWL_EXSTYLE);
        SetWindowLong(h, GWL_EXSTYLE, s | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
    }

    public static void ClickThrough(IntPtr h, bool on)
    {
        int s = GetWindowLong(h, GWL_EXSTYLE);
        int n = on ? (s | WS_EX_TRANSPARENT) : (s & ~WS_EX_TRANSPARENT);
        if (n != s) SetWindowLong(h, GWL_EXSTYLE, n);
    }
}
