// The caret after an IME conversion. UI Toolkit (the advanced text generator of this Unity version) receives the text an IME
// commits as character key events before the event that ends the composition. The text field still shows its composition
// preview at that moment, so after the first committed character it saves the caret again and, when the composition ends,
// restores that stale index: "suzuki" converted to 鈴木 leaves the caret between 鈴 and 木, and with continuous typing the next
// composition is inserted there. A trickle-down key handler on the panel ends the focused field's preview (the caret goes back
// to where the composition started) before a committed character reaches the field, so the characters are inserted in place
// and the end of the composition changes nothing. The field's editing state is internal to UI Toolkit and is reached by
// reflection, resolved once; when a member is missing (another Unity version) the fix stays off and says so once in the log.
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Input
{
    public static class ImeCommitFix
    {
        /// <summary>Off only in tests that show the engine's own behaviour.</summary>
        public static bool Enabled = true;
        /// <summary>Previews ended before a committed character (tests).</summary>
        public static int Applied { get; private set; }
        /// <summary>Why the fix is unavailable (a UI Toolkit member not found), or null.</summary>
        public static string Problem { get; private set; }

        const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static PropertyInfo editingManipulator, keyboardHandler;
        static FieldInfo editingUtilities, compositionActive, compositionString;
        static MethodInfo restoreCursor;
        static bool? available;

        /// <summary>True when the UI Toolkit members the fix needs exist.</summary>
        public static bool Available
        {
            get
            {
                if (available.HasValue) return available.Value;
                try
                {
                    editingManipulator = typeof(TextElement).GetProperty("editingManipulator", Members);
                    var manipulator = editingManipulator?.PropertyType;
                    editingUtilities = manipulator?.GetField("editingUtilities", Members);
                    keyboardHandler = manipulator?.GetProperty("keyboardEditingEventHandler", Members);
                    var utilities = editingUtilities?.FieldType;
                    compositionActive = utilities?.GetField("isCompositionActive", Members);
                    restoreCursor = utilities?.GetMethod("RestoreCursorState", Members, null, Type.EmptyTypes, null);
                    compositionString = typeof(TextElement).Assembly.GetType("UnityEngine.UIElements.KeyboardTextEditorEventHandler")?.GetField("m_compositionString", Members);
                    string missing = editingManipulator == null ? "TextElement.editingManipulator" : editingUtilities == null ? "editingUtilities"
                        : keyboardHandler == null ? "keyboardEditingEventHandler" : compositionActive == null ? "isCompositionActive"
                        : restoreCursor == null ? "RestoreCursorState" : compositionString == null ? "m_compositionString" : null;
                    Problem = missing == null ? null : "UI Toolkit member not found: " + missing;
                }
                catch (Exception e) { Problem = "UI Toolkit members unreadable: " + e.Message; }
                available = Problem == null;
                if (Problem != null) Debug.LogWarning("[ui] the IME commit fix is off (" + Problem + "); the caret after an IME conversion may land inside the converted text");
                return available.Value;
            }
        }

        /// <summary>Listens for key events on the panel of <paramref name="anyElement"/> (its visual tree when attached, else the element).</summary>
        public static void Attach(VisualElement anyElement)
        {
            var at = anyElement?.panel?.visualTree ?? anyElement;
            if (at == null) return;
            at.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            at.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        static void OnKeyDown(KeyDownEvent e)
        {
            if (!Enabled || e.character == '\0') return;
            var field = EditingText(e.target as VisualElement);
            if (field != null) EndPreview(field);
        }

        /// <summary>The text element that edits for <paramref name="v"/> (itself, or the first one inside a text input field), or null.</summary>
        public static TextElement EditingText(VisualElement v)
        {
            if (v == null || !Available) return null;
            if (v is TextElement t && editingManipulator.GetValue(t) != null) return t;
            if (!v.ClassListContains("unity-base-text-field")) return null;
            TextElement found = null;
            v.Query<TextElement>().ForEach(x => { if (found == null && editingManipulator.GetValue(x) != null) found = x; });
            return found;
        }

        /// <summary>True while the field shows a composition preview.</summary>
        public static bool Composing(TextElement field)
        {
            var u = Utilities(field);
            return u != null && (bool)compositionActive.GetValue(u);
        }

        /// <summary>
        /// Ends the field's composition preview when one is open: the caret goes back to where the composition started and the
        /// field forgets the composition string (the event that ends the composition then changes nothing). True when it acted.
        /// </summary>
        public static bool EndPreview(TextElement field)
        {
            try
            {
                var u = Utilities(field);
                if (u == null || !(bool)compositionActive.GetValue(u)) return false;
                restoreCursor.Invoke(u, null);
                compositionActive.SetValue(u, false);
                var handler = keyboardHandler.GetValue(editingManipulator.GetValue(field));
                if (handler != null && compositionString.DeclaringType.IsInstanceOfType(handler)) compositionString.SetValue(handler, string.Empty);
                Applied++;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ui] the IME commit fix failed: " + e.Message);
                return false;
            }
        }

        static object Utilities(TextElement field)
        {
            if (field == null || !Available) return null;
            var m = editingManipulator.GetValue(field);
            return m != null ? editingUtilities.GetValue(m) : null;
        }
    }
}
