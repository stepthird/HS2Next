// One app per UserData (Unity-free): the app opens <UserData>/app.lock exclusively and holds it for the process lifetime, so a
// second app on the same folder (which would share the bridge's endpoint and token, the options, the crash marker and the
// quarantine) is refused with a clear message. The file is opened with no sharing and deleted on close, so the operating
// system releases it when the process ends, also after a crash. Within one process the lock of a folder is held once: taking
// it again (an Editor play session after one that did not end cleanly) closes the earlier handle first.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace HS2Next.App.Setup
{
    public sealed class InstanceLock : IDisposable
    {
        /// <summary>The lock file's name inside UserData.</summary>
        public const string FileName = "app.lock";

        static readonly Dictionary<string, InstanceLock> held = new Dictionary<string, InstanceLock>(StringComparer.OrdinalIgnoreCase);

        FileStream stream;

        /// <summary>The lock file (absolute).</summary>
        public string Path { get; }
        public bool IsHeld => stream != null;

        InstanceLock(string path, FileStream s)
        {
            Path = path;
            stream = s;
        }

        /// <summary>The message of a refused lock (the settings stage's error).</summary>
        public static string RefusedMessage(string userData) => "HS2Next is already running with this folder: " + userData;

        /// <summary>
        /// Takes the lock of <paramref name="userData"/> (the folder must exist). Returns null and sets <paramref name="error"/> when
        /// another process holds it (or the file cannot be opened for another reason, named in the error).
        /// </summary>
        public static InstanceLock TryAcquire(string userData, out string error)
        {
            error = null;
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(userData, FileName));
            lock (held)
            {
                if (held.TryGetValue(path, out var mine)) mine.Release();
                FileStream s;
                try
                {
                    s = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
                }
                catch (IOException e) when (IsSharingViolation(e))
                {
                    error = RefusedMessage(userData);
                    return null;
                }
                catch (Exception e) when (AppFolders.IsFolderError(e))
                {
                    error = "the lock " + path + " cannot be opened: " + e.GetType().Name + ": " + e.Message;
                    return null;
                }
                try
                {
                    var text = Encoding.ASCII.GetBytes("pid " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "\nstarted " +
                                                       DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "\n");
                    s.SetLength(0);
                    s.Write(text, 0, text.Length);
                    s.Flush();
                }
                catch (IOException) { }   // the content is informational
                var l = new InstanceLock(path, s);
                held[path] = l;
                return l;
            }
        }

        // ERROR_SHARING_VIOLATION (32) or ERROR_LOCK_VIOLATION (33) as an HRESULT
        static bool IsSharingViolation(IOException e)
        {
            int code = e.HResult & 0xFFFF;
            return code == 32 || code == 33 || e.Message.IndexOf("Sharing violation", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void Release()
        {
            lock (held)
            {
                try { stream?.Dispose(); } catch (IOException) { }
                stream = null;
                if (held.TryGetValue(Path, out var cur) && ReferenceEquals(cur, this)) held.Remove(Path);
            }
        }

        /// <summary>Releases the lock (the file is deleted). Idempotent.</summary>
        public void Dispose() => Release();
    }
}
