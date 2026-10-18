using System;
using System.Runtime.InteropServices;

namespace TGShared;

// Hides/shows the BepInEx console window. Logging to BepInEx\LogOutput.log is not affected.
// The console is created by BepInEx before plugins load, so it can still flash for a moment at start-up;
// to never create it, set [Logging.Console] Enabled = false in BepInEx\config\BepInEx.cfg.
internal static class ConsoleWindow
{
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    internal static void Apply(bool show)
    {
        try
        {
            var h = GetConsoleWindow();
            if (h != IntPtr.Zero) ShowWindow(h, show ? 5 : 0);
        }
        catch { }
    }
}
