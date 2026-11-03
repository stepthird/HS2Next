// Deleting user files from the browsers: after the UI's confirmation, to the OS
// recycle bin: Windows SHFileOperation with FOF_ALLOWUNDO (silent, no confirmation: the UI asked already).
// Recycle (the file lists of characters, coordinates and poses) never deletes permanently: it refuses when the file is not on
// a fixed local drive (removable and network drives have no recycle bin) or the shell call fails, and asks Windows to warn
// (FOF_WANTNUKEWARNING) should the shell still find no room for it in the recycle bin. Delete (the scene browser) falls back to
// File.Delete elsewhere or when the shell call fails.
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HS2Next.UI.Panels
{
    public static class RecycleBin
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        const uint FO_DELETE = 3;
        const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;

        /// <summary>True when the last <see cref="Delete"/> or <see cref="Recycle"/> went to the recycle bin.</summary>
        public static bool LastRecycled { get; private set; }

        /// <summary>Tests: when set, <see cref="Recycle"/> returns this problem without calling the shell (the recycle bin
        /// "is not available"). Restore null after use.</summary>
        public static string TestUnavailable;

        /// <summary>Moves the file to the recycle bin (Windows) or deletes it. Returns null on success, else the problem.</summary>
        public static string Delete(string path)
        {
            LastRecycled = false;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "file not found";
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                try
                {
                    var op = new SHFILEOPSTRUCT
                    {
                        wFunc = FO_DELETE,
                        pFrom = Path.GetFullPath(path) + "\0\0",
                        fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
                    };
                    if (SHFileOperation(ref op) == 0 && op.fAnyOperationsAborted == 0 && !File.Exists(path)) { LastRecycled = true; return null; }
                }
                catch (Exception) { }
            }
            try { File.Delete(path); return null; }
            catch (Exception e) { return e.Message; }
        }

        /// <summary>The problem that keeps <paramref name="path"/> from the recycle bin before anything is tried (null = none):
        /// not Windows, not found, not on a fixed local drive.</summary>
        public static string Unavailable(string path)
        {
            if (TestUnavailable != null) return TestUnavailable;
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return "no recycle bin on this system";
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "file not found";
            try
            {
                string full = Path.GetFullPath(path);
                if (full.StartsWith(@"\\", StringComparison.Ordinal)) return "no recycle bin for a network folder";
                var drive = new DriveInfo(Path.GetPathRoot(full));
                if (drive.DriveType != DriveType.Fixed) return "no recycle bin on this drive (" + drive.DriveType + ")";
            }
            catch (Exception e) { return "the drive could not be checked: " + e.Message; }
            return null;
        }

        /// <summary>
        /// Moves the file to the recycle bin and never deletes it otherwise. Returns null when the file went to the recycle bin,
        /// else the problem (the file is left where it was).
        /// </summary>
        public static string Recycle(string path)
        {
            LastRecycled = false;
            string why = Unavailable(path);
            if (why != null) return why;
            try
            {
                var op = new SHFILEOPSTRUCT
                {
                    wFunc = FO_DELETE,
                    pFrom = Path.GetFullPath(path) + "\0\0",
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
                };
                int rc = SHFileOperation(ref op);
                if (rc == 0 && op.fAnyOperationsAborted == 0 && !File.Exists(path)) { LastRecycled = true; return null; }
                return op.fAnyOperationsAborted != 0 ? "cancelled" : "the recycle bin refused the file (code " + rc + ")";
            }
            catch (Exception e) { return "the recycle bin is not available: " + e.Message; }
        }
    }
}
