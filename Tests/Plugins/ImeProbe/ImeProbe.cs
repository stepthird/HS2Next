// The IME caret probe (CC0-1.0): a test plugin of the opt-in IME caret check. It records, for the text field holding the
// keyboard focus, the value, the cursor and selection indices, the shown text and the composition string - per frame
// (when something changed) and at every key and IME event of the panel (trickle-down, before the field handles it) - and
// switches the Japanese text service of this process (TSF, for this process only), so that the check can run with a
// second IME and give the user's back. Every reply is text the check typed itself, indices and ids.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using UnityEngine;
using UnityEngine.UIElements;

namespace Stp3.Test.ImeProbe
{
    public sealed class ImeProbePlugin : IPlugin, IPluginUpdate
    {
        IPluginContext ctx;
        readonly List<Dictionary<string, object>> log = new List<Dictionary<string, object>>();
        int seq;
        string lastSig;
        VisualElement hooked;
        bool imeHooked;
        GameObject go;
        static readonly Type ImeEventType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.IMEEvent");

        public void Load(IPluginContext context)
        {
            ctx = context;
            go = new GameObject("imeprobe late") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Late>().Owner = this;
            Reg("imeprobe.field", "The focused text field (or the named one): value, cursor, selection, shown text, composition.", c =>
            {
                Hook();
                var name = c.Args.Get<string>("name", null);
                var root = Root();
                VisualElement e = name == null ? Focused() : root?.Q(name);
                var d = Describe("field", e);
                d["root"] = root != null;
                d["imeIsSelected"] = Input.imeIsSelected;
                d["imeMode"] = Input.imeCompositionMode.ToString();
                return d;
            });
            Reg("imeprobe.log", "Log entries from index `since`.", c =>
            {
                int since = c.Args.Get("since", 0);
                return new Dictionary<string, object> { ["next"] = seq, ["entries"] = log.Where(x => (int)x["i"] >= since).ToList() };
            });
            Reg("imeprobe.clear", "Clears the log.", c => { log.Clear(); lastSig = null; return new Dictionary<string, object> { ["next"] = seq }; });
            Reg("imeprobe.tsf", "op active (default) / list / activate {clsid} - the keyboard text service of this process.", c =>
            {
                string op = c.Args.Get("op", "active");
                if (op == "list") return new Dictionary<string, object> { ["profiles"] = Tsf.Profiles(0x411).Select(Tsf.Describe).ToList() };
                if (op == "activate")
                {
                    var clsid = new Guid(c.Args.Require<string>("clsid"));
                    var p = Tsf.Profiles(0x411).FirstOrDefault(x => x.clsid == clsid && x.dwProfileType == Tsf.ProfileTypeTip);
                    if (p.clsid != clsid) throw new ArgumentException("no Japanese text service " + clsid);
                    int hr = Tsf.Activate(p);
                    return new Dictionary<string, object> { ["hr"] = hr, ["active"] = Tsf.Describe(Tsf.ActiveProfile()) };
                }
                return new Dictionary<string, object> { ["active"] = Tsf.Describe(Tsf.ActiveProfile()) };
            });
            Reg("imeprobe.find", "An element by name, or by class (cls), under the element named `under`: its input part's bounds (panel px); scroll: true scrolls it into view first.", c =>
            {
                var root = Root();
                var under = c.Args.Get<string>("under", null);
                var scope = under == null ? root : root?.Q(under);
                var cls = c.Args.Get<string>("cls", null);
                var e = cls != null ? scope?.Q(className: cls) : scope?.Q(c.Args.Require<string>("name"));
                if (e == null) return new Dictionary<string, object> { ["found"] = false };
                if (c.Args.Get("scroll", false)) e.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(e);
                var input = e.Q(className: TextField.inputUssClassName) ?? e;
                var wb = input.worldBound;
                return new Dictionary<string, object>
                {
                    ["found"] = true, ["type"] = e.GetType().Name, ["enabled"] = e.enabledInHierarchy,
                    ["worldBound"] = new[] { wb.x, wb.y, wb.width, wb.height },
                };
            });
        }

        void Reg(string name, string doc, CommandHandler h)
        {
            var spec = new CommandSpec(name, doc) { AllowExtraArgs = true };
            ctx.Commands.Register(spec, h);
        }

        public void Unload()
        {
            if (go) UnityEngine.Object.Destroy(go);
        }

        public void Update(in PluginFrame frame) { Hook(); Record("update", null); }

        internal void LateTick() { Hook(); Record("late", null); }

        // ---------------------------------------------------------------- the panel

        static VisualElement Root()
        {
            foreach (var tn in new[] { "HS2Next.UI.Setup.SetupHost", "HS2Next.UI.UiHost" })
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = a.GetType(tn, false);
                    if (t == null) continue;
                    var cur = t.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (cur == null) break;
                    if (cur is UnityEngine.Object uo && !uo) break;
                    if (t.GetProperty("Closed")?.GetValue(cur) is bool closed && closed) break;
                    var r = t.GetProperty("Root")?.GetValue(cur) as VisualElement;
                    if (r != null && r.panel != null) return r;
                    break;
                }
            }
            return null;
        }

        static readonly MethodInfo LeafMethod = typeof(FocusController).GetMethod("GetLeafFocusedElement",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        /// <summary>The innermost focused element: UI Toolkit's focusedElement names the outermost composite (the tree while its
        /// rename field has the keyboard).</summary>
        static VisualElement Focused()
        {
            var fc = Root()?.panel?.focusController;
            if (fc == null) return null;
            try { if (LeafMethod?.Invoke(fc, null) is VisualElement leaf) return leaf; } catch (Exception) { }
            return fc.focusedElement as VisualElement;
        }

        void Hook()
        {
            var vt = Root()?.panel?.visualTree;
            if (vt == null || vt == hooked) return;
            if (hooked != null)
            {
                hooked.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
                hooked.UnregisterCallback<ChangeEvent<string>>(OnChange, TrickleDown.TrickleDown);
            }
            hooked = vt;
            vt.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            vt.RegisterCallback<ChangeEvent<string>>(OnChange, TrickleDown.TrickleDown);
            imeHooked = false;
            if (ImeEventType != null)
            {
                try
                {
                    var mi = typeof(ImeProbePlugin).GetMethod(nameof(OnIme), BindingFlags.NonPublic | BindingFlags.Instance).MakeGenericMethod(ImeEventType);
                    var cb = Delegate.CreateDelegate(typeof(EventCallback<>).MakeGenericType(ImeEventType), this, mi);
                    var reg = typeof(CallbackEventHandler).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .First(m => m.Name == "RegisterCallback" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 2)
                        .MakeGenericMethod(ImeEventType);
                    reg.Invoke(vt, new object[] { cb, TrickleDown.TrickleDown });
                    imeHooked = true;
                }
                catch (Exception e) { ctx?.Log.Warning("imeprobe: IME event hook failed: " + e.Message); }
            }
        }

        void OnKey(KeyDownEvent e)
        {
            var x = new Dictionary<string, object> { ["key"] = e.keyCode.ToString(), ["ch"] = e.character == '\0' ? "" : e.character.ToString(), ["mods"] = e.modifiers.ToString() };
            Record("key", x, force: true);
        }

        void OnChange(ChangeEvent<string> e)
        {
            var ft = FieldOf(Focused());
            if (ft == null || (e.target != ft && FieldOf(e.target as VisualElement) != ft)) return;
            var x = new Dictionary<string, object> { ["old"] = e.previousValue, ["new"] = e.newValue, ["target"] = (e.target as VisualElement)?.name };
            Record("change", x, force: true);
        }

        void OnIme<T>(T e) where T : EventBase
        {
            var cs = ImeEventType.GetProperty("compositionString")?.GetValue(e) as string;
            Record("ime", new Dictionary<string, object> { ["evComp"] = cs ?? "" }, force: true);
        }

        static TextField FieldOf(VisualElement f) => f as TextField ?? f?.GetFirstAncestorOfType<TextField>();

        Dictionary<string, object> Describe(string kind, VisualElement f)
        {
            var tf = FieldOf(f);
            var d = new Dictionary<string, object>
            {
                ["kind"] = kind, ["frame"] = Time.frameCount, ["comp"] = Input.compositionString ?? "",
                ["focus"] = f == null ? null : string.IsNullOrEmpty(f.name) ? f.GetType().Name : f.name,
            };
            if (tf != null)
            {
                d["field"] = tf.name;
                d["value"] = tf.value;
                d["cursor"] = tf.cursorIndex;
                d["select"] = tf.selectIndex;
                var te = tf.Q(className: TextField.inputUssClassName)?.Q<TextElement>();
                d["shown"] = te?.text;
                d["generator"] = te?.resolvedStyle.unityTextGenerator.ToString();
                if (te != null)
                {
                    // the caret's bottom-left (panel px) as the app's candidate-window fix computes it
                    var cp = tf.textSelection.cursorPosition;
                    var wb = te.worldBound;
                    d["caretPanel"] = new[] { wb.x + cp.x, wb.y + cp.y };
                }
            }
            return d;
        }

        void Record(string kind, Dictionary<string, object> extra, bool force = false)
        {
            var d = Describe(kind, Focused());
            string sig = d.TryGetValue("field", out var fn) ? fn + "|" + d["value"] + "|" + d["cursor"] + "|" + d["select"] + "|" + d["shown"] + "|" + d["comp"] : "nofield|" + d["focus"] + "|" + d["comp"];
            if (!force && sig == lastSig) return;
            lastSig = sig;
            if (extra != null) foreach (var kv in extra) d[kv.Key] = kv.Value;
            d["i"] = seq++;
            d["imeHooked"] = imeHooked;
            log.Add(d);
            if (log.Count > 4000) log.RemoveAt(0);
        }

        sealed class Late : MonoBehaviour
        {
            internal ImeProbePlugin Owner;
            void LateUpdate() { try { Owner?.LateTick(); } catch (Exception) { } }
        }
    }

    /// <summary>The keyboard text service of this process (ITfInputProcessorProfileMgr, manual vtable; in-process only).</summary>
    static class Tsf
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Profile
        {
            public uint dwProfileType;
            public ushort langid;
            public Guid clsid;
            public Guid guidProfile;
            public Guid catid;
            public IntPtr hklSubstitute;
            public uint dwCaps;
            public IntPtr hkl;
            public uint dwFlags;
        }

        [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr r, uint coinit);
        [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr ppv);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ActivateProfileFn(IntPtr self, uint type, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetActiveProfileFn(IntPtr self, ref Guid catid, out Profile p);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumProfilesFn(IntPtr self, ushort langid, out IntPtr ppEnum);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumNextFn(IntPtr self, uint count, out Profile p, out uint fetched);

        static Guid ClsidProfiles = new Guid("33C53A50-F456-4884-B049-85FD643ECFED");
        static Guid IidProfileMgr = new Guid("71C6E74C-0F28-11D8-A82A-00065B84435C");
        static Guid CatKeyboard = new Guid("34745C63-B2F0-4784-8B67-5E12C8701A31");
        public const uint ProfileTypeTip = 1;
        const uint FlagForProcess = 0x10000000, FlagDontCareLanguage = 0x4;

        static T Fn<T>(IntPtr obj, int slot) where T : Delegate
        {
            IntPtr vtbl = Marshal.ReadIntPtr(obj);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size), typeof(T));
        }

        static IntPtr Mgr()
        {
            CoInitializeEx(IntPtr.Zero, 2);
            int hr = CoCreateInstance(ref ClsidProfiles, IntPtr.Zero, 1, ref IidProfileMgr, out IntPtr p);
            if (hr != 0) throw new Exception($"CoCreateInstance 0x{hr:X8}");
            return p;
        }

        static void Release(IntPtr p) { if (p != IntPtr.Zero) Fn<ReleaseFn>(p, 2)(p); }

        public static Profile ActiveProfile()
        {
            var m = Mgr();
            try
            {
                int hr = Fn<GetActiveProfileFn>(m, 10)(m, ref CatKeyboard, out var prof);
                if (hr != 0) throw new Exception($"GetActiveProfile 0x{hr:X8}");
                return prof;
            }
            finally { Release(m); }
        }

        public static List<Profile> Profiles(ushort langid)
        {
            var list = new List<Profile>();
            var m = Mgr();
            IntPtr en = IntPtr.Zero;
            try
            {
                int hr = Fn<EnumProfilesFn>(m, 6)(m, langid, out en);
                if (hr != 0 || en == IntPtr.Zero) throw new Exception($"EnumProfiles 0x{hr:X8}");
                var next = Fn<EnumNextFn>(en, 4);
                while (next(en, 1, out var p, out uint got) == 0 && got == 1) list.Add(p);
            }
            finally { Release(en); Release(m); }
            return list;
        }

        public static int Activate(Profile p)
        {
            var m = Mgr();
            try
            {
                var clsid = p.clsid; var prof = p.guidProfile;
                return Fn<ActivateProfileFn>(m, 3)(m, p.dwProfileType, p.langid, ref clsid, ref prof, p.hkl, FlagForProcess | FlagDontCareLanguage);
            }
            finally { Release(m); }
        }

        public static Dictionary<string, object> Describe(Profile p) => new Dictionary<string, object>
        {
            ["type"] = p.dwProfileType, ["langid"] = p.langid, ["clsid"] = p.clsid.ToString("B").ToUpperInvariant(),
            ["profile"] = p.guidProfile.ToString("B").ToUpperInvariant(), ["flags"] = p.dwFlags,
        };
    }
}
