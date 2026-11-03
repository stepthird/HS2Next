// UiListsV12Test's clean-up of the Windows recycle bin: the files the test deleted (always from its sandbox) are taken out of
// the recycle bin again at the end of the run, so a run leaves nothing there. Only the entries whose original folder ("deleted
// from") lies under the sandbox root are touched; every other entry is left alone. The entries are read through the shell's
// recycle-bin folder (the shell folder of the recycle bin, its items and their "deleted from" property); an entry is removed by
// deleting its two files in the recycle bin's folder of the drive ($R… the content, $I… where it came from). Windows only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class RecycleBinSandboxPurge
{
    [DllImport("shell32.dll")] static extern int SHGetDesktopFolder(out IntPtr ppshf);
    [DllImport("shell32.dll")] static extern int SHGetSpecialFolderLocation(IntPtr hwnd, int csidl, out IntPtr ppidl);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int StrRetToBuf(IntPtr pstr, IntPtr pidl, StringBuilder buf, uint cch);
    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint coInit);
    [DllImport("oleaut32.dll")] static extern int VariantClear(IntPtr pvarg);

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid fmtid; public uint pid; }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int BindToObjectFn(IntPtr self, IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumObjectsFn(IntPtr self, IntPtr hwnd, uint flags, out IntPtr ppenum);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDisplayNameOfFn(IntPtr self, IntPtr pidl, uint flags, IntPtr strret);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDetailsExFn(IntPtr self, IntPtr pidl, ref PropertyKey key, IntPtr variant);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int NextFn(IntPtr self, uint celt, out IntPtr pidl, out uint fetched);

    const int CSIDL_BITBUCKET = 0x000a;
    const uint SHCONTF_FOLDERS = 0x20, SHCONTF_NONFOLDERS = 0x40, SHCONTF_INCLUDEHIDDEN = 0x80, SHGDN_FORPARSING = 0x8000;
    const ushort VT_BSTR = 8;
    static readonly Guid IID_IShellFolder2 = new Guid("93F2F68C-1D1B-11d3-A30E-00C04F79ABD1");
    // System.Recycle.DeletedFrom
    static readonly PropertyKey DeletedFrom = new PropertyKey { fmtid = new Guid("9B174B33-40FF-11D2-A27E-00C04FC30871"), pid = 2 };

    static T Slot<T>(IntPtr obj, int slot) where T : class
    {
        IntPtr vtbl = Marshal.ReadIntPtr(obj);
        return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size), typeof(T)) as T;
    }

    static void Release(IntPtr obj) { if (obj != IntPtr.Zero) Slot<ReleaseFn>(obj, 2)(obj); }

    /// <summary>One entry of the recycle bin: where it was deleted from and its content file in the recycle bin's folder.</summary>
    public struct Entry { public string DeletedFrom, BinFile; }

    /// <summary>Every entry of the recycle bin (the shell's recycle-bin folder); throws when the shell cannot be asked.</summary>
    public static List<Entry> List()
    {
        var res = new List<Entry>();
        CoInitializeEx(IntPtr.Zero, 2);   // apartment-threaded; an already initialised thread keeps its mode
        IntPtr desktop = IntPtr.Zero, pidlBin = IntPtr.Zero, bin = IntPtr.Zero, en = IntPtr.Zero;
        try
        {
            int hr = SHGetDesktopFolder(out desktop);
            if (hr != 0) throw new InvalidOperationException("SHGetDesktopFolder 0x" + hr.ToString("X8"));
            hr = SHGetSpecialFolderLocation(IntPtr.Zero, CSIDL_BITBUCKET, out pidlBin);
            if (hr != 0) throw new InvalidOperationException("the recycle bin's location 0x" + hr.ToString("X8"));
            var iid = IID_IShellFolder2;
            hr = Slot<BindToObjectFn>(desktop, 5)(desktop, pidlBin, IntPtr.Zero, ref iid, out bin);   // IShellFolder::BindToObject
            if (hr != 0) throw new InvalidOperationException("the recycle bin's folder 0x" + hr.ToString("X8"));
            hr = Slot<EnumObjectsFn>(bin, 4)(bin, IntPtr.Zero, SHCONTF_FOLDERS | SHCONTF_NONFOLDERS | SHCONTF_INCLUDEHIDDEN, out en);   // EnumObjects
            if (hr != 0 || en == IntPtr.Zero) return res;   // an empty recycle bin may give S_FALSE and no enumerator
            var next = Slot<NextFn>(en, 3);
            var nameOf = Slot<GetDisplayNameOfFn>(bin, 11);
            var details = Slot<GetDetailsExFn>(bin, 17);   // IShellFolder2::GetDetailsEx
            IntPtr strret = Marshal.AllocCoTaskMem(272), variant = Marshal.AllocCoTaskMem(24);
            try
            {
                while (next(en, 1, out IntPtr child, out uint got) == 0 && got == 1)
                {
                    try
                    {
                        string binFile = null, from = null;
                        if (nameOf(bin, child, SHGDN_FORPARSING, strret) == 0)
                        {
                            var sb = new StringBuilder(1024);
                            if (StrRetToBuf(strret, child, sb, (uint)sb.Capacity) == 0) binFile = sb.ToString();
                        }
                        for (int i = 0; i < 24; i++) Marshal.WriteByte(variant, i, 0);
                        var key = DeletedFrom;
                        if (details(bin, child, ref key, variant) == 0)
                        {
                            if ((ushort)Marshal.ReadInt16(variant) == VT_BSTR) from = Marshal.PtrToStringBSTR(Marshal.ReadIntPtr(variant, 8));
                            VariantClear(variant);
                        }
                        res.Add(new Entry { DeletedFrom = from, BinFile = binFile });
                    }
                    finally { Marshal.FreeCoTaskMem(child); }
                }
            }
            finally { Marshal.FreeCoTaskMem(strret); Marshal.FreeCoTaskMem(variant); }
        }
        finally
        {
            Release(en);
            Release(bin);
            if (pidlBin != IntPtr.Zero) Marshal.FreeCoTaskMem(pidlBin);
            Release(desktop);
        }
        return res;
    }

    static bool Under(string folder, string root)
    {
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(root)) return false;
        string f = folder.TrimEnd('\\', '/') + "\\", r = Path.GetFullPath(root).TrimEnd('\\', '/') + "\\";
        return f.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The entries deleted from under <paramref name="sandboxRoot"/>.</summary>
    public static List<Entry> Of(string sandboxRoot)
    {
        var l = new List<Entry>();
        foreach (var e in List()) if (Under(e.DeletedFrom, sandboxRoot)) l.Add(e);
        return l;
    }

    /// <summary>
    /// Removes the recycle-bin entries deleted from under <paramref name="sandboxRoot"/> (nothing else): their $R… and $I… files
    /// in the recycle bin's folder of the drive. Returns (entries before, removed, entries left); problems in <paramref name="problems"/>.
    /// </summary>
    public static (int before, int removed, int left) Purge(string sandboxRoot, List<string> problems)
    {
        var mine = Of(sandboxRoot);
        int removed = 0;
        foreach (var e in mine)
        {
            string r = e.BinFile;
            string name = r == null ? null : Path.GetFileName(r);
            // only a content file of a recycle bin folder ("<drive>:\$Recycle.Bin\<user>\$R…")
            if (name == null || !name.StartsWith("$R", StringComparison.Ordinal) || r.IndexOf("\\$Recycle.Bin\\", StringComparison.OrdinalIgnoreCase) < 0)
            {
                problems?.Add("skipped (not a recycle bin file): " + r);
                continue;
            }
            string i = Path.Combine(Path.GetDirectoryName(r), "$I" + name.Substring(2));
            try
            {
                if (File.Exists(r)) File.Delete(r);
                else if (Directory.Exists(r)) { problems?.Add("skipped a folder entry: " + r); continue; }
                if (File.Exists(i)) File.Delete(i);
                removed++;
            }
            catch (Exception x) { problems?.Add(r + ": " + x.Message); }
        }
        int left = Of(sandboxRoot).Count;
        return (mine.Count, removed, left);
    }
}
