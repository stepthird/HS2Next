// The state the edit units work on: what CharaAssembler.Build kept in private fields
// before 4.0 (skeleton dictionaries, hips, part objects and rows, creators, draw materials, shape engines, accessory
// slots, the mask records, the log, the texture-guard scope) lives here and in CharaContext, so a unit can run after the
// build. One per built character (CharaContext.Edit); the units read the documents of Input (CharaEditInput).
using System.Collections.Generic;
using HS2Next.Content;
using HS2Next.Save;

namespace HS2Next.Chara.Edit
{
    public sealed partial class EditContext
    {
        /// <summary>The character's context (objects, bones, materials, plugin results; Runtime/Chara/CharaContext.cs).</summary>
        public CharaContext Ctx;
        /// <summary>What the units read: the documents, resources, part resolver, slot table and plugin data.</summary>
        public CharaEditInput Input;
        public ContentCatalog Catalog;
        /// <summary>The assembler's log (one line per step).</summary>
        public List<string> Log;
        /// <summary>The editor of this character.</summary>
        public CharaEditor Editor;

        public CharaDocument Chara => Input?.Chara;
        public CoordDocument Coord => Input?.Coord;

        /// <summary>The report of the flush running now (null outside a flush).</summary>
        public FlushReport Report { get; private set; }
        /// <summary>True during the build's own flush (MarkAll + Flush inside CharaAssembler.Build).</summary>
        public bool InBuild { get; private set; }

        internal void Begin(FlushReport r, bool build)
        {
            Report = r;
            InBuild = build;
            BeginFlush();
        }

        internal void End()
        {
            try { EndFlush(); }
            finally { Report = null; InBuild = false; }
        }

        partial void BeginFlush();
        partial void EndFlush();
        partial void ReleaseSessionData();

        /// <summary>Releases what an edit session kept (decoded skin layers).</summary>
        internal void ReleaseSession() => ReleaseSessionData();
    }
}
