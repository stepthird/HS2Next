// Package C's add module: the browsers add.chara (right sidebar by default),
// add.item, add.anime, add.map, add.light, add.pose; the actions add.female / add.male (args {path}: add that card at
// once; else show the character browser of that sex), add.item (args {ref: "guid#id"} or the browser), add.light (args
// {ref} or the browser); and the viewport as a drop target for browser rows: an add dropped on the viewport is placed at
// the surface under the pointer (map or object; else the camera pivot) in the same history entry, an animation / pose
// dropped on a character applies to it.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class AddModule : IUiModule
    {
        public int Order => 110;

        public static ListBrowser<StudioItem> ItemBrowser { get; private set; }
        public static ListBrowser<StudioAnime> AnimeBrowser { get; private set; }
        public static ListBrowser<ListBrowsers.MapRow> MapBrowser { get; private set; }
        public static ListBrowser<StudioLight> LightBrowser { get; private set; }
        public static PoseBrowser Poses { get; private set; }
        public static ViewportDrop Drop { get; private set; }

        public void Register(UiContext ctx)
        {
            var p = ctx.Registry.Panels;
            p.Register(CharaBrowser.PanelId, "panel.add.chara", LayoutRegions.Right, c => new CharaBrowser(c), ActionScope.Browser, 10);
            p.Register("add.item", "panel.add.item", LayoutRegions.Right, c => ItemBrowser = new ListBrowser<StudioItem>(c, ListBrowsers.Items(c)), ActionScope.Browser, 20);
            p.Register("add.anime", "panel.add.anime", LayoutRegions.Right, c => AnimeBrowser = new ListBrowser<StudioAnime>(c, ListBrowsers.Anime(c)), ActionScope.Browser, 30);
            p.Register("add.map", "panel.add.map", LayoutRegions.Right, c =>
            {
                var b = MapBrowser = new ListBrowser<ListBrowsers.MapRow>(c, ListBrowsers.Maps(c));
                c.SceneChanged += batch => { if (batch.Touches(null, "map")) b.List.RefreshItems(); };
                c.SceneLoaded += _ => b.List.RefreshItems();
                return b;
            }, ActionScope.Browser, 40);
            p.Register("add.light", "panel.add.light", LayoutRegions.Right, c => LightBrowser = new ListBrowser<StudioLight>(c, ListBrowsers.Lights(c)), ActionScope.Browser, 50);
            p.Register(PoseBrowser.PanelId, "panel.add.pose", LayoutRegions.Right, c => Poses = new PoseBrowser(c), ActionScope.Browser, 60);

            var a = ctx.Registry.Actions;
            a.Register(ActionIds.AddFemale, call => AddChara(ctx, call, 1));
            a.Register(ActionIds.AddMale, call => AddChara(ctx, call, 0));
            a.Register(ActionIds.AddItem, call =>
            {
                if (call.Arg("ref") is string r && ContentId.TryParse(r, out var id))
                {
                    PanelKit.ApplyPlacement(ctx);
                    return ctx.ShowResult(ctx.Ops.AddItem(id, null, null, -1, UiContext.User()));
                }
                CInput.ShowPanel(ctx, "add.item");
                return null;
            });
            a.Register(ActionIds.AddLight, call =>
            {
                if (call.Arg("ref") is string r && ContentId.TryParse(r, out var id))
                {
                    PanelKit.ApplyPlacement(ctx);
                    return ctx.ShowResult(ctx.Ops.AddLight(id, null, null, -1, UiContext.User()));
                }
                CInput.ShowPanel(ctx, "add.light");
                return null;
            });
            Drop = new ViewportDrop(ctx);
            PanelKit.ApplyPlacement(ctx);
            ctx.Options.Changed += k => { if (k == OptionDefs.AddPlacement) PanelKit.ApplyPlacement(ctx); };
        }

        static object AddChara(UiContext ctx, ActionInvocation call, int sex)
        {
            if (call.Arg("path") is string path && path.Length > 0)
            {
                PanelKit.ApplyPlacement(ctx);
                bool card = call.Arg("useCardState") is bool b ? b : ctx.Options.Bool(OptionDefs.AddUseCardState);
                var r = ctx.ShowResult(ctx.Ops.AddChara(path, null, null, -1, new CardAddOptions { UseCardState = card }, UiContext.User()));
                return r?.NewIds.FirstOrDefault();
            }
            CInput.ShowPanel(ctx, CharaBrowser.PanelId);
            CharaBrowser.Current?.ShowSex(sex);
            return null;
        }
    }

    /// <summary>The viewport as a drop target of browser rows (adds at the surface hit; animation / pose onto a character).</summary>
    public sealed class ViewportDrop : IDropTarget
    {
        readonly UiContext ctx;
        public ViewportDrop(UiContext ctx)
        {
            this.ctx = ctx;
            DragController.Register(this);
        }

        public VisualElement DropElement => ctx.Viewport?.Element;

        public void Leave() { }

        public bool Hover(DragPayload payload, Vector2 panelPos, out string reason)
        {
            reason = null;
            switch (payload)
            {
                case AddPayload _: return true;
                case ApplyPayload ap:
                {
                    var id = CInput.PickNode(ctx, panelPos, out _);
                    var n = id == null ? null : ctx.Model.Find(id);
                    if (n == null || n.Kind != ap.Kind) { reason = L10n.Get("workspace.dropOnChara"); return false; }
                    return true;
                }
                default:
                    reason = L10n.Get("workspace.dropWrongKind");
                    return false;
            }
        }

        public void Drop(DragPayload payload, Vector2 panelPos)
        {
            if (payload is ApplyPayload ap)
            {
                var id = CInput.PickNode(ctx, panelPos, out _);
                if (id != null) ctx.ShowResult(ap.Apply(id));
                return;
            }
            if (!(payload is AddPayload add)) return;
            AddAt(add, panelPos);
        }

        /// <summary>Adds at the surface under <paramref name="panelPos"/> (map or object), else at the camera pivot; one entry.</summary>
        public OpResult AddAt(AddPayload add, Vector2 panelPos)
        {
            CInput.PickNode(ctx, panelPos, out Vector3? hit);
            using (var edit = ctx.BeginEdit(add.OpLabel ?? OpLabels.AddItem))
            {
                var r = add.Add(null, null);
                if (r == null || !r.Ok) { edit.Cancel(); return ctx.ShowResult(r); }
                if (hit != null && r.NewIds.Count > 0)
                {
                    var s = ctx.Ops.SetTransform(r.NewIds, hit.Value, null, null, UiContext.User());
                    if (!s.Ok) ctx.ShowResult(s);
                }
                var c = edit.Commit();
                if (!c.Ok) ctx.ShowResult(c);
                return r;
            }
        }
    }
}
