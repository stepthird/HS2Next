// Plain record types of the Studio list database.
// One record per list row that survived parsing. Column names follow the spec; columns the
// game does not read are kept only where a later phase may want them (noted per field).
// Records are filled by StudioDb's loader and are read-only by convention afterwards.
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Content.Studio
{
    /// <summary>The Studio list kinds, in the per-bundle processing order.</summary>
    public enum StudioListKind
    {
        AccessoryPointGroup, Bone, ItemGroup, ItemCategory, ItemList, ItemBoneList, ItemData, Light,
        AnimeGroup, AnimeCategory, Anime, HAnime, VoiceGroup, VoiceCategory, Voice, Bgm, Map, Filter, Probe, StudioSky,
    }

    /// <summary>A record that names an asset in a bundle.</summary>
    public interface IStudioAsset
    {
        /// <summary>The zipmod that defined the row, or null for vanilla rows. Its bundles are searched first.</summary>
        ZipmodInfo Zipmod { get; }
        /// <summary>abdata-relative bundle path as written in the row.</summary>
        string BundlePath { get; }
        /// <summary>Asset name (prefab, controller, clip, texture, material) or, for maps, the scene name.</summary>
        string AssetName { get; }
        /// <summary>Manifest column ("" when the kind or row has none). Only dependency manifests declared by a zipmod matter.</summary>
        string Manifest { get; }
    }

    /// <summary>Common part of every list record.</summary>
    public abstract class StudioRecord
    {
        /// <summary>
        /// (source GUID, local id). Vanilla: (illusion.hs2, column-0 number); zipmods: (manifest GUID,
        /// the number as written in the CSV). For Anime/HAnime/Voice the id is <c>no</c> and the full
        /// key is (GUID, group, category, no).
        /// </summary>
        public ContentId Key;

        /// <summary>The namespace GUID (<see cref="ContentIds.Vanilla"/> for base-game rows).</summary>
        public string Guid => Key.SourceGuid;

        /// <summary>True for base-game rows.</summary>
        public bool IsVanilla => Key.IsVanilla;

        /// <summary>The zipmod that defined the row; null for vanilla rows.</summary>
        public ZipmodInfo Zipmod { get; internal set; }

        /// <summary>Name of the list the row came from (asset name, or CSV file name without extension).</summary>
        public string ListName;

        /// <summary>The (real or Sideloader virtual) info bundle of that list, e.g. "studio/info/00.unity3d".</summary>
        public string ListBundle;

        /// <summary>Row index inside the (merged) list, header rows included.</summary>
        public int Row;

        /// <summary>Global load-order position of this definition.</summary>
        public long Order;

        /// <summary>Display position: the load-order position of the first definition of the key (first insertion).</summary>
        public long Position;

        /// <summary>Content the game hides without an AI-Shoujo install. Loaded, but hidden while <see cref="StudioLoadOptions.EmulateAIInstallGate"/> is on.</summary>
        public bool AiContent;

        /// <summary>Replaced by another definition of the same key (last wins); still reachable through <see cref="StudioDb.Shadowed"/>.</summary>
        public bool Shadowed;

        /// <summary>Shown by the default enumerations (not shadowed, not gated, group and category known where applicable).</summary>
        public bool Visible { get; internal set; }

        public override string ToString() => $"{GetType().Name} {Key} [{ListBundle}:{ListName}#{Row}]";
    }

    // ---------------------------------------------------------------- groups / categories

    /// <summary>
    /// An item, anime or voice group. Group numbers are ONE global space shared by vanilla and all
    /// mods: the last definition wins for <see cref="Sort"/> and <see cref="Name"/>.
    /// </summary>
    public sealed class StudioGroup
    {
        public StudioListKind Kind;          // ItemGroup, AnimeGroup or VoiceGroup
        public int Number;
        public int Sort;
        public string Name;
        /// <summary>Load-order position of the first definition (tie-break after <see cref="Sort"/>).</summary>
        public long Position;
        /// <summary>GUIDs that declared this group (vanilla = illusion.hs2), in first-declaration order.</summary>
        public readonly List<string> DeclaredBy = new List<string>();
        /// <summary>Distinct names seen in the definitions, in order (more than one = name conflict).</summary>
        public readonly List<string> Names = new List<string>();
        /// <summary>The categories attached to this group, in display order (sort, then first insertion), hidden ones included.</summary>
        public readonly List<StudioCategory> Categories = new List<StudioCategory>();
        /// <summary>True if at least one visible record is in the group.</summary>
        public bool Visible { get; internal set; }

        public override string ToString() => $"{Kind} {Number} \"{Name}\" (sort {Sort})";
    }

    /// <summary>A category of a group; key (group, number), global like groups.</summary>
    public sealed class StudioCategory
    {
        public StudioListKind Kind;          // ItemCategory, AnimeCategory or VoiceCategory
        public int Group;
        public int Number;
        public int Sort;
        public string Name;
        public long Position;
        public readonly List<string> DeclaredBy = new List<string>();
        public readonly List<string> Names = new List<string>();
        /// <summary>AI-gated category (anime group 0, categories 1-12) with no non-gated definition.</summary>
        public bool AiContent;
        /// <summary>
        /// Every definition was read before its group existed, so the game drops it.
        /// HS2Next attaches it anyway unless <see cref="StudioLoadOptions.EmulateLoadOrderDrops"/>.
        /// </summary>
        public bool DroppedInGame;
        /// <summary>The group is defined somewhere (the category is attached to it).</summary>
        public bool HasGroup;
        /// <summary>True if at least one visible record is in the category.</summary>
        public bool Visible { get; internal set; }

        public override string ToString() => $"{Kind} {Group}/{Number} \"{Name}\" (sort {Sort})";
    }

    // ---------------------------------------------------------------- items

    /// <summary>ItemList row: key (GUID, no); group/category are attributes.</summary>
    public sealed class StudioItem : StudioRecord, IStudioAsset
    {
        public int Group, Category;
        /// <summary>Column 0 (`管理番号`) = <see cref="StudioRecord.Key"/>.Id.</summary>
        public int No => Key.Id;
        public string Name;
        public string Manifest;
        public string BundlePath;
        /// <summary>Prefab asset name (`ファイルパス`).</summary>
        public string FileName;
        /// <summary>Column 7 (`子の接続先`), NOT used by the game (the attach point is ItemComponent.childRoot). Kept for tools.</summary>
        public string ChildAttach;
        /// <summary>Numbers from the list file name (bundle number xx, group, category); they only order and gate.</summary>
        public int FileBundleNo, FileGroup, FileCategory;
        /// <summary>FK bone names (ItemBoneList), or null.</summary>
        public IReadOnlyList<string> Bones { get; internal set; }
        /// <summary>ItemData colour flags (vanilla only), or null.</summary>
        public StudioItemColor Color { get; internal set; }

        string IStudioAsset.AssetName => FileName;
        string IStudioAsset.BundlePath => BundlePath;
        string IStudioAsset.Manifest => Manifest;
        ZipmodInfo IStudioAsset.Zipmod => Zipmod;

        public override string ToString() => $"Item {Key} {Group}/{Category} \"{Name}\" {BundlePath}:{FileName}";
    }

    /// <summary>One ItemBoneList row after attachment; kept for diagnostics.</summary>
    public sealed class StudioItemBoneList : StudioRecord
    {
        /// <summary>Group/category from the file name (used for vanilla parity only).</summary>
        public int FileGroup, FileCategory;
        public int ItemNo => Key.Id;
        public string[] Bones;
    }

    /// <summary>ItemData colour flags, key (group, category, id), vanilla only.</summary>
    public sealed class StudioItemColor
    {
        public int Group, Category, Id;
        /// <summary>[colour 1, colour 2, colour 3, glass] used.</summary>
        public readonly bool[] Colors = new bool[4];
        /// <summary>Number of true flags: list text colour 1 red, 2 cyan, 3 green, 4 yellow, 0 white.</summary>
        public int FlagCount { get { int n = 0; foreach (var c in Colors) if (c) n++; return n; } }
        public string ListName;
    }

    // ---------------------------------------------------------------- anime / voice

    /// <summary>An option item of an Anime/HAnime row.</summary>
    public sealed class StudioAnimeOption
    {
        public string Bundle, Asset, Manifest;
        /// <summary>Base RuntimeAnimatorController of the option prefab.</summary>
        public string ControllerBundle, ControllerAsset;
        /// <summary>Override controller.</summary>
        public string OverrideBundle, OverrideAsset;
        /// <summary>Raw parentage string: comma-separated `parent[/child]` transform names.</summary>
        public string Parentage;
        /// <summary>Keep a counter-scale against the character height.</summary>
        public bool CounterScale;
        /// <summary>Follow the character's speed/pattern (always true for HAnime).</summary>
        public bool IsAnimeSync = true;

        /// <summary>Parsed <see cref="Parentage"/>: (parent transform, child transform of the prefab or null).</summary>
        public IEnumerable<(string parent, string child)> ParentagePairs()
        {
            if (string.IsNullOrEmpty(Parentage)) yield break;
            foreach (var p in Parentage.Split(','))
            {
                if (p.Length == 0) continue;
                int s = p.IndexOf('/');
                yield return s < 0 ? (p, null) : (p.Substring(0, s), p.Substring(s + 1));
            }
        }
    }

    /// <summary>
    /// Anime or HAnime row; both share ONE key space (group, category, no).
    /// Key = (GUID, no); the full key is (GUID, <see cref="Group"/>, <see cref="Category"/>, <see cref="No"/>).
    /// </summary>
    public sealed class StudioAnime : StudioRecord, IStudioAsset
    {
        /// <summary>True for HAnime rows.</summary>
        public bool IsH;
        /// <summary>Column 0, stored but not used by the list UI.</summary>
        public int Sort;
        public int Group, Category;
        public int No => Key.Id;
        public string Name;
        /// <summary>Bundle and asset of the RuntimeAnimatorController.</summary>
        public string BundlePath, FileName;
        /// <summary>Animator state to play.</summary>
        public string Clip;
        public readonly List<StudioAnimeOption> Options = new List<StudioAnimeOption>();

        // HAnime only
        public string OverrideBundle, OverrideFile;
        /// <summary>Breast layer (TryInt, failure = 0, so an empty cell means layer 0).</summary>
        public int BreastLayer;
        public string YureBundle, YureFile;
        public int MotionId;
        /// <summary>Column 14 `揺れ誰`.</summary>
        public int YureWho;
        public bool IsMotion;
        /// <summary>Eight PV (IK target copy) flags; default true. Plain Anime: the first four are enabled at play time.</summary>
        public readonly bool[] PvFlags = { true, true, true, true, true, true, true, true };

        /// <summary>Winner of the global (group, category, no) index (the game's last-wins record).</summary>
        public bool GlobalWinner { get; internal set; }
        /// <summary>Display position of the raw (group, category, no) key: first insertion over all GUIDs.</summary>
        public long GlobalPosition;
        public int FileBundleNo;

        string IStudioAsset.AssetName => FileName;
        string IStudioAsset.BundlePath => BundlePath;
        string IStudioAsset.Manifest => "";
        ZipmodInfo IStudioAsset.Zipmod => Zipmod;

        public override string ToString() => $"{(IsH ? "HAnime" : "Anime")} {Guid} {Group}/{Category}/{No} \"{Name}\" {BundlePath}:{FileName}:{Clip}";
    }

    /// <summary>Voice row; key (GUID, group, category, no).</summary>
    public sealed class StudioVoice : StudioRecord, IStudioAsset
    {
        public int Group, Category;
        public int No => Key.Id;
        public string Name;
        /// <summary>Bundle and AudioClip asset.</summary>
        public string BundlePath, FileName;
        public bool GlobalWinner { get; internal set; }
        /// <summary>Display position of the raw (group, category, no) key: first insertion over all GUIDs.</summary>
        public long GlobalPosition;

        string IStudioAsset.AssetName => FileName;
        string IStudioAsset.BundlePath => BundlePath;
        string IStudioAsset.Manifest => "";
        ZipmodInfo IStudioAsset.Zipmod => Zipmod;
    }

    // ---------------------------------------------------------------- single-key kinds

    /// <summary>Map row. The bundle is a streamed-scene bundle; <see cref="FileName"/> is the scene name.</summary>
    public sealed class StudioMap : StudioRecord, IStudioAsset
    {
        public int No => Key.Id;
        public string Name;
        public string BundlePath;
        /// <summary>Scene name.</summary>
        public string FileName;
        public string Manifest;
        /// <summary>`遮蔽` (vanish) bundle/asset; read but unused by HS2.</summary>
        public string VanishBundle, VanishFile;

        string IStudioAsset.AssetName => FileName;
        string IStudioAsset.BundlePath => BundlePath;
        string IStudioAsset.Manifest => Manifest;
        ZipmodInfo IStudioAsset.Zipmod => Zipmod;
    }

    /// <summary>Light target.</summary>
    public enum StudioLightTarget { All = 0, Chara = 1, Map = 2 }

    /// <summary>Light row.</summary>
    public sealed class StudioLight : StudioRecord, IStudioAsset
    {
        public int No => Key.Id;
        public string Name, Manifest, BundlePath;
        /// <summary>Prefab asset name.</summary>
        public string FileName;
        /// <summary>Raw target number (0 All, 1 Chara, 2 Map).</summary>
        public int Target;
        public StudioLightTarget TargetKind => (StudioLightTarget)Target;

        string IStudioAsset.AssetName => FileName;
        string IStudioAsset.BundlePath => BundlePath;
        string IStudioAsset.Manifest => Manifest;
        ZipmodInfo IStudioAsset.Zipmod => Zipmod;
    }

    /// <summary>A row with (no, name, bundle, asset): BGM (AudioClip), Filter (LUT texture), Probe (cubemap texture).</summary>
    public abstract class StudioSimpleAsset : StudioRecord, IStudioAsset
    {
        public int No => Key.Id;
        public string Name, BundlePath, FileName;

        string IStudioAsset.AssetName => FileName;
        string IStudioAsset.BundlePath => BundlePath;
        string IStudioAsset.Manifest => "";
        ZipmodInfo IStudioAsset.Zipmod => Zipmod;
    }

    /// <summary>BGM row.</summary>
    public sealed class StudioBgm : StudioSimpleAsset { }
    /// <summary>Filter (LUT) row.</summary>
    public sealed class StudioFilter : StudioSimpleAsset { }
    /// <summary>Probe (reflection cubemap) row.</summary>
    public sealed class StudioProbe : StudioSimpleAsset { }

    /// <summary>StudioSky row.</summary>
    public sealed class StudioSky : StudioRecord, IStudioAsset
    {
        public int No => Key.Id;
        public string Name;
        /// <summary>Sun rotation (Euler x, y, z).</summary>
        public Vector3 SunRotation;
        public string BundlePath;
        /// <summary>Skybox material asset name.</summary>
        public string FileName;
        public string Manifest;

        string IStudioAsset.AssetName => FileName;
        string IStudioAsset.BundlePath => BundlePath;
        string IStudioAsset.Manifest => Manifest;
        ZipmodInfo IStudioAsset.Zipmod => Zipmod;
    }

    /// <summary>Character FK bone row.</summary>
    public sealed class StudioBone : StudioRecord
    {
        public int No => Key.Id;
        /// <summary>Transform name (`取得先`).</summary>
        public string TransformName;
        public string Name;
        /// <summary>`分類` (7-9 = hair bones under the head).</summary>
        public int Group;
        /// <summary>`操作` (level 2 rows are skipped for females).</summary>
        public int Level;
        /// <summary>Sync partner bone no, -1 = none.</summary>
        public int SyncNo = -1;
    }

    /// <summary>AccessoryPointGroup row.</summary>
    public sealed class StudioAccessoryPointGroup : StudioRecord
    {
        public int No => Key.Id;
        public string Name;
        /// <summary>Raw `対応箇所`, e.g. "1-5".</summary>
        public string Range;
        /// <summary>Parsed inclusive accessory-slot range, or (-1, -1) when unparsable.</summary>
        public int First = -1, Last = -1;
    }
}
