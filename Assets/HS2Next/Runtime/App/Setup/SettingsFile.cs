// Writing the settings file (Config/settings.local.json under the data root) without losing what a user wrote (Unity-free):
// the file is read as a JSON object, the given keys are set ("block.member" inside its object block, created when absent;
// a null value removes the member), every other member - comments ("_…" members), keys of a later version, unknown keys -
// keeps its value and its place, and the result is written to a temporary file that then replaces the file. Used by the
// first-run guide, app.setup finish, app.settings set and the Options panel's settings sections.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.App.Setup
{
    public static class SettingsFile
    {
        /// <summary>The file as an object (a new object when it is absent). Throws FormatException when it does not parse.</summary>
        public static JObject Read(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new JObject();
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text)) return new JObject();
            try { return JObject.Parse(text); }
            catch (Exception e) { throw new FormatException(path + ": not a JSON object (" + e.Message + ")"); }
        }

        /// <summary>The value of a key ("hs2Root", "bridge.port") in a settings object, or null when absent.</summary>
        public static JToken Get(JObject o, string key)
        {
            if (o == null || string.IsNullOrEmpty(key)) return null;
            int dot = key.IndexOf('.');
            if (dot < 0) return o.TryGetValue(key, out var v) ? v : null;
            return o[key.Substring(0, dot)] is JObject b && b.TryGetValue(key.Substring(dot + 1), out var w) ? w : null;
        }

        /// <summary>Sets (or with null removes) a key in a settings object; true when the object changed.</summary>
        public static bool Set(JObject o, string key, JToken value)
        {
            int dot = key.IndexOf('.');
            JObject target = o;
            string member = key;
            if (dot >= 0)
            {
                string block = key.Substring(0, dot);
                member = key.Substring(dot + 1);
                target = o[block] as JObject;
                if (target == null)
                {
                    if (value == null || value.Type == JTokenType.Null) return false;
                    if (o[block] != null && o[block].Type != JTokenType.Null) throw new FormatException(block + " must be an object");
                    target = new JObject();
                    o[block] = target;
                }
            }
            if (value == null || value.Type == JTokenType.Null)
                return target.Remove(member);
            var old = target[member];
            if (old != null && JToken.DeepEquals(old, value)) return false;
            target[member] = value.DeepClone();   // an existing member keeps its place; a new one goes last
            return true;
        }

        /// <summary>
        /// Writes the keys into the file (creating its folder), keeping every other member and the order; returns the keys whose
        /// value changed (nothing is written when none did). Throws FormatException when the file does not parse.
        /// </summary>
        public static List<string> Write(string path, IEnumerable<KeyValuePair<string, JToken>> values)
        {
            var o = Read(path);
            var changed = new List<string>();
            foreach (var kv in values)
                if (Set(o, kv.Key, kv.Value)) changed.Add(kv.Key);
            if (changed.Count > 0 || !File.Exists(path)) WriteAtomic(path, o.ToString(Formatting.Indented) + "\n");
            return changed;
        }

        /// <summary>UTF-8 without BOM to &lt;path&gt;.tmp-&lt;pid&gt;, then a replace of the file (a move when it is new).</summary>
        public static void WriteAtomic(string path, string text)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp-" + System.Diagnostics.Process.GetCurrentProcess().Id;
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
            }
        }
    }
}
