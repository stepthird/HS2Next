// The character inspector's Costume tab (Studio's tab 4): the outfit cards of the primary's sex in the coordinate browser
// (HS2's files and HS2Next's own, name / date sort, the 3.5 thumbnail grid) and «読込» (`costume-load`; a double click on a
// card does the same), which wears the whole coordinate — clothes, accessories, their rules and plugin data, the name — on
// every selected character of that sex (StudioOps.ApplyCoordinateFile with mode all, one entry "Load coordinate"); the
// characters' clothes state is kept. The browser is built when the tab is first shown. v1.2 (owner item 6): «読込» rebuilds the
// character inspector (the character is built again), and with it this tab; the browser keeps its sort, search, chosen card and
// scroll per sex for the app run (CoordinateBrowser's view state), so the list stays as it was.
using System;
using System.Collections.Generic;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class CharaCostumeTab : VisualElement
    {
        public const string TabId = "costume";

        readonly UiContext ctx;
        readonly string primary;
        readonly TargetSelector sel = TargetSelector.Selection(NodeKind.Chara);
        Button loadButton;

        /// <summary>The browser (null until the tab is first shown).</summary>
        public CoordinateBrowser Browser { get; private set; }
        /// <summary>The last load's result (tests).</summary>
        public OpResult LastResult { get; private set; }

        /// <summary>Adds the Costume tab to the character inspector's tab strip (the one line of CharaInspector).</summary>
        public static void AddTo(UiContext ctx, string primaryId, VisualElement strip, IDictionary<string, Button> buttons,
            IDictionary<string, VisualElement> bodies, Action<string> show)
        {
            var b = new Button(() => show(TabId)) { name = "tab-" + TabId };
            b.AddToClassList("hs-inspector__tab");
            Loc.Bind(b, "inspector.tab.costume");
            strip.Add(b);
            buttons[TabId] = b;
            bodies[TabId] = new CharaCostumeTab(ctx, primaryId);
        }

        public CharaCostumeTab(UiContext ctx, string primaryId)
        {
            this.ctx = ctx;
            primary = primaryId;
            name = "tab-" + TabId + "-body";
            AddToClassList("hs-costume-tab");
            RegisterCallback<AttachToPanelEvent>(_ => Build());
        }

        void Build()
        {
            if (Browser != null) return;
            int sex = StudioOps.SexOf(ctx.Model.Find(primary));
            Browser = new CoordinateBrowser(ctx, sex < 0 ? 1 : sex, "costume-browser", FileBrowserKind.Coordinate, keepChoice: true);
            Browser.Grid.HintKey = "browser.costume.hint";
            Browser.Activated += e => Load(e?.FullPath);
            Browser.SelectionChanged += _ => loadButton?.SetEnabled(Browser.Selected != null);
            Add(Browser);
            loadButton = InspectorUi.Button("maker.coord.load", () => Load(Browser.Selected?.FullPath), "costume-load");
            loadButton.SetEnabled(Browser.Selected != null);   // the kept card may be chosen already
            Add(loadButton);
        }

        /// <summary>Wears the coordinate file <paramref name="path"/> (all of it) on the selected characters, as «読込» does.</summary>
        public OpResult Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return LastResult = OpResult.NothingToDo();
            var targets = sel.Resolve(ctx);
            if (targets.Count == 0) return LastResult = OpResult.NothingToDo();
            return LastResult = ctx.ShowResult(ctx.Ops.ApplyCoordinateFile(targets, path, CoordinateApplyMode.All, UiContext.User(OpLabels.LoadCoordinate)));
        }
    }
}
