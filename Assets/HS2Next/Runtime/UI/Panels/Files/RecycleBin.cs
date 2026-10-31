// Deleting user files from the browsers: after the UI's confirmation, to the OS recycle bin
// when available, else deleted: Windows SHFileOperation with FOF_ALLOWUNDO (silent, no confirmation: the UI asked already);
// elsewhere, or when the shell call fails, File.Delete. Only HS2Next's own files are ever passed (HS2 files are read only).
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
        const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;

        /// <summary>True when the last <see cref="Delete"/> went to the recycle bin.</summary>
        public static bool LastRecycled { get; private set; }

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
    }
}
