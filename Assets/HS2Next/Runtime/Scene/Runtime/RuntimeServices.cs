// What the scene realization needs from the application: the content catalog,
// the Studio lists, the HS2 install root (card references are relative to it) and the runtime switches. Also
// builds the SessionOptions the Unity layer gives a session: attach points from the AccessoryPointGroup list,
// the joint-correction default of new characters, list-row labels and the catalog's importer (Phase 4.6: the
// session imports a card-backed node at its first edit and at save with it when none is registered).
using System;
using System.Collections.Generic;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Motion.Corrective;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class SceneRuntimeSettings
    {
        /// <summary>
        /// Characters are stepped only by <see cref="SceneRuntime.Tick"/> (CharaMotion.AutoStep false). Default true outside
        /// play mode and false in play mode or a player.
        /// </summary>
        public bool ManualStepping = !Application.isPlaying;
        /// <summary>Deleted characters kept for an instant undo.</summary>
        public int GraveyardMaxCharas = 4;
        /// <summary>Deleted non-character nodes kept for an instant undo.</summary>
        public int GraveyardMaxNodes = 500;
    }

    public sealed class RuntimeServices
    {
        public ContentCatalog Catalog;
        /// <summary>The Studio lists (normally <c>Catalog.LoadStudioLists()</c>).</summary>
        public StudioDb Db;
        /// <summary>The HS2 install root (absolute); card sources record their path relative to it.</summary>
        public string Hs2Root;
        /// <summary>HS2Next's UserData (absolute; `user` file references of item patterns and panels),
        /// or null (such references are then missing).</summary>
        public string UserData;
        public SceneRuntimeSettings Settings = new SceneRuntimeSettings();
        /// <summary>Extension modules of every character build (both builders take a copy per build).
        /// Tests switch modules here (e.g. ABMX off in the HS2 comparison). Null = plugin data off.</summary>
        public CharaExtOptions ExtOptions = new CharaExtOptions();

        CardCache cards;
        /// <summary>Parsed cards and their bytes by SHA-256, shared by every runtime of these services.</summary>
        public CardCache Cards => cards ??= new CardCache();

        CardSourceFactory factory;
        /// <summary>Card sources from files.</summary>
        public CardSourceFactory CardSources => factory ??= new CardSourceFactory(this);

        /// <summary>
        /// Session options for a scene realized with these services: attach points from <c>StudioDb.AccessoryPointGroups</c>
        /// (vanilla table when the list is empty), `correction` of new characters all true when
        /// <c>CorrectSetup.InitialCategoriesOn</c>, labels from the list rows, the importer of the catalog (the session prefers a
        /// registered CharaImport.Importer).
        /// </summary>
        public SessionOptions NewSessionOptions(FindingList findings = null)
        {
            var o = new SessionOptions();
            if (Db != null && Db.AccessoryPointGroups.Count > 0)
            {
                var rows = new List<AttachPointGroup>();
                foreach (var g in Db.AccessoryPointGroups.All) rows.Add(new AttachPointGroup(g.No, g.Name, g.First, g.Last));
                o.AttachPoints = AttachPointTable.FromGroups(rows, findings);
            }
            if (CorrectSetup.InitialCategoriesOn) for (int i = 0; i < o.CorrectionDefault.Length; i++) o.CorrectionDefault[i] = true;
            o.LabelProvider = new RuntimeLabelProvider(Db);
            if (Catalog != null) o.Importer = ImportSetup.ImporterFor(Catalog);
            return o;
        }
    }

    /// <summary>Default labels with list row names for items and lights.</summary>
    public sealed class RuntimeLabelProvider : DefaultLabelProvider
    {
        readonly StudioDb db;
        public RuntimeLabelProvider(StudioDb db) { this.db = db; }

        public override string DefaultLabel(TreeNode node)
        {
            if (node != null && db != null)
            {
                // an accessory offered as an item is named by its accessory row (its id repeats item ids)
                if (node.State is ItemNodeState it && ContentId.TryParse(it.Ref, out var iid) && !iid.IsEmpty
                    && (it.Group == StudioDb.AccessoryGroup ? db.TryGetAccessoryItem(it.Category, iid, out var row) : db.TryGetItem(iid, out row))
                    && !string.IsNullOrEmpty(row.Name))
                    return row.Name;
                if (node.State is LightNodeState li && ContentId.TryParse(li.Ref, out var lid) && !lid.IsEmpty && db.TryGetLight(lid, out var lrow) && !string.IsNullOrEmpty(lrow.Name))
                    return lrow.Name;
            }
            return base.DefaultLabel(node);
        }
    }
}
