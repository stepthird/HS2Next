// UiShellTest.play group IM (Phase 5.3 package U): the caret after an IME conversion. A text field on the Studio panel gets
// the events an IME produces, in the engine's order - composition updates, then the committed text as character key events,
// then the composition's end - with the panel's composition string faked to match each event (no real IME in a batch run):
//   conv  "すずき" -> "鈴木", commit, then "x": 鈴木x with the caret at the end (the defect left the caret between 鈴 and 木);
//   cont  "鈴木" committed while the next composition "ｓ" starts, then "さ" committed: 鈴木さ, then "x" at the end;
//   one   a single committed character (Enter on "あ");
//   off   the conv sequence with the fix switched off: the engine's own caret, reported as information (the defect present or not).
// The real IME (both Japanese text services, real keystrokes) is the opt-in player check Tools/ime_caret_check.py.
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HS2Next.UI.Input;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiShellTest
{
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterImeGroup()
    {
        if (!Groups.Any(g => g.id == "IM")) Groups.Add(("IM", ImeCommit));
    }

    static readonly Type ImeEventType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.IMEEvent");
    static readonly FieldInfo PanelComposition = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.BaseVisualElementPanel")
        ?.GetField("IMEGetCompositionString", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>The panel's composition string while the group runs (what Input.compositionString says during a real composition).</summary>
    static string fakeComposition = "";

    static void ImeEvent(VisualElement target, string composition)
    {
        fakeComposition = composition;
        var getPooled = ImeEventType.GetMethod("GetPooled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
        // the engine sends the composition's change twice per frame, each with the composition string of that moment
        for (int k = 0; k < 2; k++)
        {
            var e = (EventBase)getPooled.Invoke(null, new object[] { composition });
            target.SendEvent(e);
            ((IDisposable)e).Dispose();
        }
    }

    static void Committed(VisualElement target, string text, string nextComposition)
    {
        fakeComposition = nextComposition;
        foreach (char ch in text)
            using (var e = KeyDownEvent.GetPooled(ch, KeyCode.None, EventModifiers.None)) target.SendEvent(e);
        ImeEvent(target, nextComposition);
    }

    static void Typed(VisualElement target, char ch)
    {
        using (var e = KeyDownEvent.GetPooled(ch, KeyCode.None, EventModifiers.None)) target.SendEvent(e);
    }

    static IEnumerator ImeCommit(Ctx c)
    {
        c.Check(ImeCommitFix.Available, "the UI Toolkit members of the IME commit fix exist (" + (ImeCommitFix.Problem ?? "ok") + ")");
        c.Check(ImeEventType != null && PanelComposition != null, "the test reaches the IME event and the panel's composition string");
        if (ImeEventType == null || PanelComposition == null || !ImeCommitFix.Available) yield break;
        var root = c.Host.Root;
        var panel = root.panel;
        var original = PanelComposition.GetValue(panel);
        PanelComposition.SetValue(panel, (Func<string>)(() => fakeComposition));
        var tf = new TextField { name = "u53-ime-field" };
        tf.style.position = Position.Absolute;
        tf.style.left = 40;
        tf.style.top = 40;
        tf.style.width = 320;
        root.Add(tf);
        bool wasEnabled = ImeCommitFix.Enabled;
        try
        {
            yield return PlayModeHarness.WaitFrames(2);
            var te = ImeCommitFix.EditingText(tf);
            c.Check(te != null, "the field's editing text element found");
            if (te == null) yield break;
            c.Info("text generator of the field: " + te.resolvedStyle.unityTextGenerator);
            foreach (var mode in new[] { "conv", "cont", "one", "off" })
            {
                ImeCommitFix.Enabled = mode != "off";
                tf.value = "";
                tf.Focus();
                yield return PlayModeHarness.WaitFrames(1);
                // the engine sends the IME's events to the focused element (the field, which hands them to its text element)
                var focused = panel.focusController.focusedElement as VisualElement;
                if (focused != te && focused != tf) { c.Check(false, mode + ": the field has the focus (" + (focused?.name ?? focused?.GetType().Name ?? "none") + ")"); continue; }
                var to = focused;
                int applied0 = ImeCommitFix.Applied;
                if (mode == "one")
                {
                    ImeEvent(to, "ａ");
                    ImeEvent(to, "あ");
                    Committed(to, "あ", "");
                }
                else
                {
                    ImeEvent(to, "ｓ");
                    ImeEvent(to, "す");
                    ImeEvent(to, "すずき");
                    ImeEvent(to, "鈴木");
                    c.Check(tf.value == "鈴木" && tf.cursorIndex == 2, mode + ": the preview shows 鈴木 with the caret after it (" + tf.value + ", " + tf.cursorIndex + ")");
                    if (mode == "cont")
                    {
                        Committed(to, "鈴木", "ｓ");
                        c.Check(tf.value == "鈴木ｓ" && tf.cursorIndex == 3, "cont: the next composition starts after the committed text (" + tf.value + ", caret " + tf.cursorIndex + ")");
                        ImeEvent(to, "さ");
                        Committed(to, "さ", "");
                    }
                    else Committed(to, "鈴木", "");
                }
                yield return PlayModeHarness.WaitFrames(1);
                string committed = tf.value;
                int caret = tf.cursorIndex, sel = tf.selectIndex;
                Typed(to, 'x');
                yield return PlayModeHarness.WaitFrames(1);
                string expect = mode == "cont" ? "鈴木さ" : mode == "one" ? "あ" : "鈴木";
                if (mode == "off")
                    c.Info("without the fix the engine leaves the caret at " + caret + " of " + committed.Length + " (" + committed + " + x -> " + tf.value + ")" +
                           (caret == committed.Length ? ": the engine no longer needs the fix" : ": the defect the fix works around"));
                else
                {
                    c.Check(committed == expect && caret == expect.Length && sel == caret, mode + ": committed " + committed + ", caret " + caret + "/" + sel + " (expected " + expect + ", " + expect.Length + ")");
                    c.Check(tf.value == expect + "x", mode + ": a letter typed next goes to the end (" + tf.value + ")");
                    c.Check(ImeCommitFix.Applied > applied0, mode + ": the fix ended the preview before the committed text (" + (ImeCommitFix.Applied - applied0) + "x)");
                }
            }
        }
        finally
        {
            ImeCommitFix.Enabled = wasEnabled;
            fakeComposition = "";
            PanelComposition.SetValue(panel, original);
            tf.RemoveFromHierarchy();
            c.Host.Context.Viewport?.Element?.Focus();
        }
    }
}
