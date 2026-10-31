// A plugin's settings file <UserData>/Plugins/<guid>/settings.json (Unity-free): read as text, written atomically (a
// temporary file next to it, then a replace) after checking that the text is one JSON value. The plugin owns the
// schema. The folder is created on the first write, never on a read.
using System;
using System.IO;
using System.Text;
using HS2Next.Api.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.App.Plugins
{
    public sealed class PluginSettingsFile : IPluginSettings
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        readonly Func<bool> valid;

        public PluginSettingsFile(string dataDirectory, Func<bool> valid = null)
        {
            Path = System.IO.Path.Combine(dataDirectory, "settings.json");
            this.valid = valid ?? (() => true);
        }

        public string Path { get; }

        public string Read()
        {
            Check();
            return File.Exists(Path) ? File.ReadAllText(Path, Utf8) : null;
        }

        public void Write(string json)
        {
            Check();
            if (json == null) throw new ArgumentNullException(nameof(json));
            try
            {
                using (var r = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
                {
                    JToken.ReadFrom(r);
                    if (r.Read()) throw new JsonReaderException("additional text after the JSON value");
                }
            }
            catch (JsonException e) { throw new ArgumentException("the settings must be one JSON value: " + e.Message, nameof(json)); }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            string tmp = Path + ".tmp" + System.Diagnostics.Process.GetCurrentProcess().Id;
            File.WriteAllText(tmp, json, Utf8);
            if (File.Exists(Path)) File.Replace(tmp, Path, null);
            else File.Move(tmp, Path);
        }

        void Check()
        {
            if (!valid()) throw new ObjectDisposedException("plugin settings", "the plugin has unloaded");
        }
    }
}
