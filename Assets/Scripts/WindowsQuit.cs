using System;
using System.Runtime.InteropServices;

namespace RoadRage.UnityRemake
{
    /// The Windows player crashed on every exit with a window (UnityPlayer.dll, in
    /// RuntimeStatic&lt;PlatformAccessibilityManager&gt;::StaticDestroy: an access violation at
    /// null + 0xe0, Unity 6000.5.4f1). It is an engine bug in a static destructor that runs
    /// after everything else has shut down; the headless self-test never creates the window
    /// and exits cleanly. A crashing process also stayed alive while Windows and Unity's crash
    /// handler wrote their dumps - eight test runs once held 7 GB between them.
    ///
    /// Nothing is left to do at that point, so once the game's own quit work is saved the
    /// process ends itself with exit code 0, before the static destructors run.
    /// shortcut: skips Unity's native shutdown; drop this once a Unity update fixes the destructor.
    public static class WindowsQuit
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint exitCode);

        public static void EndNow()
        {
            UnityEngine.PlayerPrefs.Save();
            TerminateProcess(GetCurrentProcess(), 0);
        }
#else
        public static void EndNow() { }
#endif
    }
}
