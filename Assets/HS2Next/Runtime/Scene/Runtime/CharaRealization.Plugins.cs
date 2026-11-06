// The plugin parts of a character node (used by the engine side of the plugin Api, Runtime/App/Plugins/Engine):
//   skin layers  the layers plugins set outside builds are kept here across rebuilds (PluginLayers), joined to every build
//                of this node after its BodyMesh hooks (CharaBuildHooks.BuildingRecord around the build), and a change marks
//                the skin for the next flush (KsoxDirty with the unit ext.textures): the skin is re-composited, the character
//                is not rebuilt. They go with this realization (a new or loaded scene, a forgotten node).
//   rebuilds     a plugin's request rebuilds the character at the next flush point instead of flushing it (requests before
//                that merge into one rebuild); counted apart from the edit rebuilds.
// Without plugins nothing here is used.
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.Chara.Edit;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class CharaRealization
    {
        /// <summary>The skin layers plugins set on this node outside builds (every plugin, in the order set).</summary>
        public SkinLayerRecord PluginLayers { get; } = new SkinLayerRecord();

        readonly List<string> pluginRebuilds = new List<string>();

        /// <summary>Rebuilds that ran for plugins' requests (across rebuilds).</summary>
        public int PluginRebuilds { get; private set; }
        /// <summary>The plugins whose requests the last such rebuild ran for, or null.</summary>
        public string LastPluginRebuild { get; private set; }

        /// <summary>True while a plugin's rebuild request waits for the next flush point.</summary>
        public bool PluginRebuildPending => pluginRebuilds.Count > 0;

        /// <summary>
        /// A plugin (<paramref name="owner"/>, its GUID) asks for a rebuild at the next apply; false, with nothing queued, unless
        /// the character is built. Requests before that apply merge into one rebuild.
        /// </summary>
        public bool RequestPluginRebuild(string owner)
        {
            if (State != RealizationState.Built || InGraveyard) return false;
            owner = owner ?? "";
            if (!pluginRebuilds.Contains(owner)) pluginRebuilds.Add(owner);
            CharaEditFlush.Request(this);
            return true;
        }

        /// <summary>The flush point: a pending plugin request rebuilds the character (true) instead of the flush.</summary>
        bool TakePluginRebuild()
        {
            if (pluginRebuilds.Count == 0) return false;
            string who = string.Join(", ", pluginRebuilds);
            pluginRebuilds.Clear();
            if (State != RealizationState.Built) return false;
            PluginRebuilds++;
            LastPluginRebuild = who;
            Runtime.RebuildContent(this);
            return true;
        }

        /// <summary>A plugin set or removed a skin layer of the built character: the skin is re-composited in the next flush
        /// (no rebuild). False when the character is not built.</summary>
        public bool PluginLayersChanged()
        {
            var x = Context?.Edit;
            var ed = Context?.Editor;
            if (State != RealizationState.Built || x == null || ed == null) return false;
            x.KsoxDirty = true;
            ed.MarkUnit(UnitMap.ExtTextures);
            CharaEditFlush.Request(this);
            return true;
        }
    }
}
