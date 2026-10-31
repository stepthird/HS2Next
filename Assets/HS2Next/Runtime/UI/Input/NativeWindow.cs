// Win32 helpers of the UI (in-process only): the player window, its client-area offset inside the window rect (the IME
// candidate position of ImeCaretFix, spike S2a), its DPI, and the keyboard layout of the key labels (a Japanese keyboard
// type → JIS; spike S8 / H's K5). Non-Windows and the Editor's game view: offset 0, layout US unless detected.
using System;
using System.Runtime.InteropServices;
using HS2Next.UI.Core;
using UnityEngine;

namespace HS2Next.UI.Input
{
    public static class NativeWindow
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int GetKeyboardType(int type);
        [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, EnumProc cb, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
#endif
        static IntPtr hwnd;

        /// <summary>The player's main window (0 in the Editor or off Windows).</summary>
        public static IntPtr Window
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                if (hwnd != IntPtr.Zero) return hwnd;
                try
                {
                    var h = GetActiveWindow();
                    if (h == IntPtr.Zero)
                        EnumThreadWindows(GetCurrentThreadId(), (w, l) =>
                        {
                            var sb = new System.Text.StringBuilder(64);
                            GetClassName(w, sb, 64);
                            if (sb.ToString() == "UnityWndClass") { h = w; return false; }
                            return true;
                        }, IntPtr.Zero);
                    hwnd = h;
                }
                catch (Exception) { }
#endif
                return hwnd;
            }
        }

        /// <summary>Client-area origin minus window-rect origin (title bar and frame), physical pixels; 0 when borderless / unknown.</summary>
        public static Vector2 ClientOffset()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                var w = Window;
                if (w == IntPtr.Zero || Screen.fullScreen || !GetWindowRect(w, out var r)) return Vector2.zero;
                var p = new POINT();
                ClientToScreen(w, ref p);
                return new Vector2(p.x - r.l, p.y - r.t);
            }
            catch (Exception) { }
#endif
            return Vector2.zero;
        }

        public static int WindowDpi
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                try { return (int)GetDpiForWindow(Window); } catch (Exception) { }
#endif
                return (int)Screen.dpi;
            }
        }

        /// <summary>JIS when the keyboard type is Japanese (7) or the input language is Japanese (0x0411); else US.</summary>
        public static KeyboardLayout DetectLayout()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                if (GetKeyboardType(0) == 7) return KeyboardLayout.Jis;
                long hkl = GetKeyboardLayout(0).ToInt64();
                if ((hkl & 0xFFFF) == 0x0411) return KeyboardLayout.Jis;
            }
            catch (Exception) { }
#endif
            return KeyboardLayout.Us;
        }
    }

    /// <summary>The polled input (held keys, modifiers, mouse position): legacy Input Manager, replaceable in tests.</summary>
    public interface IInputSource
    {
        bool GetKey(KeyCode k);
        Vector3 MousePosition { get; }
        string CompositionString { get; }
        bool AnyKey { get; }
    }

    public sealed class LegacyInputSource : IInputSource
    {
        public bool GetKey(KeyCode k) => UnityEngine.Input.GetKey(k);
        public Vector3 MousePosition => UnityEngine.Input.mousePosition;
        public string CompositionString => UnityEngine.Input.compositionString ?? "";
        public bool AnyKey => UnityEngine.Input.anyKey;
    }

    /// <summary>A scripted input source (tests, ui.input): keys held until released.</summary>
    public sealed class ScriptedInputSource : IInputSource
    {
        public readonly System.Collections.Generic.HashSet<KeyCode> Down = new System.Collections.Generic.HashSet<KeyCode>();
        public Vector3 Mouse;
        public string Composition = "";
        public bool GetKey(KeyCode k) => Down.Contains(k);
        public Vector3 MousePosition => Mouse;
        public string CompositionString => Composition;
        public bool AnyKey => Down.Count > 0;
    }
}
