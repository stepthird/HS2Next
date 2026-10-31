// App options of the UI (U32): P's OptionLayers (command line > options.json > the
// settings file's `ui` block > defaults) with the file <UserData>/config/options.json read at start and written on
// change (debounced 1 s, and at quit). Command line: -uiLanguage <en|ja|zh-Hans>, -uiScale <75–200>, -uiPreset
// <default|hs2studio>, -uiOption <key>=<value> (repeatable). Set(key, value) writes the file layer; a command-line value
// keeps winning for the session (as the precedence says). Changed(key) is raised for every Set (the shell, camera
// controller, gizmos and panels re-read what they use).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.UI.Core;
using UnityEngine;

namespace HS2Next.UI
{
    public sealed class UiOptions
    {
        readonly UiContext ctx;
        public OptionLayers Layers { get; } = new OptionLayers();
        /// <summary>&lt;UserData&gt;/config.</summary>
        public string ConfigDir { get; }
        public string FilePath => Path.Combine(ConfigDir, OptionsDocument.FileName);
        public event Action<string> Changed;

        float saveAt = -1;

        internal UiOptions(UiContext ctx)
        {
            this.ctx = ctx;
            ConfigDir = Path.Combine(ctx.App.Paths?.UserData ?? Path.GetTempPath(), "config");
            Layers.Settings = ctx.App.Settings?.Ui?.Block;
            ReadCommandLine(Environment.GetCommandLineArgs());
            Load();
        }

        void ReadCommandLine(string[] a)
        {
            for (int i = 0; i + 1 < a.Length; i++)
            {
                switch (a[i])
                {
                    case "-uiLanguage": Layers.CommandLine[OptionDefs.Language] = a[++i]; break;
                    case "-uiScale": Layers.CommandLine[OptionDefs.UiScale] = a[++i]; break;
                    case "-uiPreset": Layers.CommandLine[OptionDefs.KeyPreset] = a[++i]; break;
                    case "-uiOption":
                        string kv = a[++i];
                        int eq = kv.IndexOf('=');
                        if (eq > 0 && OptionDefs.TryGet(kv.Substring(0, eq), out _)) Layers.CommandLine[kv.Substring(0, eq)] = kv.Substring(eq + 1);
                        break;
                }
            }
        }

        void Load()
        {
            if (!File.Exists(FilePath)) return;
            var problems = new List<string>();
            OptionsDocument d = null;
            try { d = OptionsDocument.Parse(File.ReadAllText(FilePath, Encoding.UTF8), problems); }
            catch (Exception e) { problems.Add(e.Message); }
            if (d != null) Layers.File = d;
            else ctx.Findings.Warning("ui.optionsInvalid", null, ctx.L10n.Get("msg.optionsInvalid") + " (" + string.Join("; ", problems) + ")");
        }

        public object Get(string key) => Layers.Get(key);
        public object Get(string key, out string source) => Layers.Get(key, out source);
        public bool Bool(string key) { try { return Layers.GetBool(key); } catch (Exception) { return OptionDefs.TryGet(key, out var d) && d.Default is bool b && b; } }
        public float Float(string key) { try { return (float)Layers.GetFloat(key); } catch (Exception) { return 0f; } }
        public int Int(string key) { try { return Layers.GetInt(key); } catch (Exception) { return 0; } }
        public string String(string key) { try { return Layers.GetString(key); } catch (Exception) { return null; } }

        /// <summary>Writes an option into options.json (saved within 1 s) and raises Changed.</summary>
        public void Set(string key, object value)
        {
            Layers.File.Set(key, value);
            saveAt = Time.realtimeSinceStartup + 1f;
            UiContext.Guard(() => Changed?.Invoke(key));
            ctx.RaiseView("options");
        }

        /// <summary>Flips a Bool option.</summary>
        public bool Toggle(string key)
        {
            bool v = !Bool(key);
            Set(key, v);
            return v;
        }

        internal void Tick()
        {
            if (saveAt >= 0 && Time.realtimeSinceStartup >= saveAt) Save();
        }

        public void Save()
        {
            saveAt = -1;
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(FilePath, Layers.File.ToJson(), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("[ui] options.json not written: " + e.Message); }
        }

        internal void Flush() { if (saveAt >= 0) Save(); }
    }

    /// <summary>The key map in use (preset + keymap.json overrides) and its resolver.</summary>
    public sealed class UiKeymap
    {
        readonly UiContext ctx;
        public Keymap Preset { get; private set; }
        public Keymap Map { get; private set; }
        public ShortcutResolver Resolver { get; private set; }
        public string PresetName => Preset?.Name;
        public KeymapOverrides Overrides { get; private set; } = new KeymapOverrides();
        public string FilePath => Path.Combine(ctx.Options.ConfigDir, "keymap.json");
        /// <summary>The keyboard layout of the key labels (auto-detected: a Japanese keyboard → Jis; option general.keyboardLayout
        /// is not in the option table, so -uiKeyboard us|jis overrides the detection).</summary>
        public KeyboardLayout Layout { get; set; }
        public event Action Changed;

        internal UiKeymap(UiContext ctx)
        {
            this.ctx = ctx;
            Layout = HS2Next.UI.Input.NativeWindow.DetectLayout();
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, "-uiKeyboard");
            if (i >= 0 && i + 1 < a.Length) Layout = a[i + 1] == "jis" ? KeyboardLayout.Jis : KeyboardLayout.Us;
            LoadOverrides();
        }

        void LoadOverrides()
        {
            if (!File.Exists(FilePath)) return;
            var problems = new List<string>();
            try { Overrides = KeymapOverrides.FromJson(File.ReadAllText(FilePath, Encoding.UTF8), problems) ?? new KeymapOverrides(); }
            catch (Exception e) { problems.Add(e.Message); }
            foreach (var p in problems) ctx.Findings.Warning("ui.keymapInvalid", null, "keymap.json: " + p);
        }

        /// <summary>Loads a preset (Resources/UI/Keymaps/&lt;name&gt;.json) with the user's overrides; reports conflicts as `ui.keymapConflict`.</summary>
        public void Use(string presetName)
        {
            if (!Keymap.PresetNames.Contains(presetName)) presetName = Keymap.DefaultPreset;
            var ta = Resources.Load<TextAsset>("UI/Keymaps/" + presetName);
            var problems = new List<string>();
            Keymap k = ta != null ? Keymap.FromJson(ta.text, problems) : null;
            if (k == null) { ctx.Findings.Error("ui.keymapMissing", null, "key preset " + presetName + " not loaded: " + string.Join("; ", problems)); k = new Keymap(presetName); }
            k.Name = presetName;
            Preset = k;
            Map = Overrides != null && (Overrides.Preset == null || Overrides.Preset == presetName) ? k.WithOverrides(Overrides) : k;
            foreach (var c in Map.Conflicts())
                ctx.Findings.Warning("ui.keymapConflict", null, ctx.L10n.Format("msg.keymapConflict", c.ActionA, c.ActionB, c.Chord.ToString(), c.Scope.ToString()));
            Resolver = new ShortcutResolver(Map);
            UiContext.Guard(() => Changed?.Invoke());
            ctx.RaiseView("preset");
        }

        /// <summary>Replaces the user's overrides (the key overlay's edits) and writes keymap.json.</summary>
        public void SetOverrides(KeymapOverrides o)
        {
            Overrides = o ?? new KeymapOverrides();
            Overrides.Preset = PresetName;
            try
            {
                Directory.CreateDirectory(ctx.Options.ConfigDir);
                File.WriteAllText(FilePath, Overrides.ToJson(), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("[ui] keymap.json not written: " + e.Message); }
            Use(PresetName);
        }

        /// <summary>The display text of an action's chords ("Ctrl+Z, Ctrl+Y"), "" when unbound.</summary>
        public string ChordText(string actionId)
        {
            if (Map == null) return "";
            var l = new List<string>();
            foreach (var c in Map.ChordsOf(actionId)) l.Add(c.ToDisplay(Layout));
            return string.Join(", ", l);
        }
    }
}
