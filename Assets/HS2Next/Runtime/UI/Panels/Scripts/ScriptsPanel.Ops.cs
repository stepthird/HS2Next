// The panel «Scene scripts»: the calls into StudioOps' scene scripts (what the script commands call), in one place.
using System.Collections.Generic;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Scripts;
using HS2Next.UI.Core;

namespace HS2Next.UI.Panels
{
    public sealed partial class ScriptsPanel
    {
        OpResult OpRename(int index, string name) => ctx.Ops.ScriptRename(index, name, UiContext.User());

        OpResult OpSetLoop(int index, bool loop) => ctx.Ops.ScriptSetLoop(index, loop, UiContext.User());

        OpResult OpRemove(int index) => ctx.Ops.ScriptRemove(index, UiContext.User());

        OpResult OpKeep(int index) => ctx.Ops.ScriptKeep(index, UiContext.User());

        OpResult OpExport(int index, string path) => ctx.Ops.ScriptExport(index, null, path);

        static string ExportedPath(OpResult r) => (r?.Value as ScriptExportResult)?.Path;

        OpResult OpCast(int index, string role, string node) =>
            ctx.Ops.ScriptCast(index, new[] { new KeyValuePair<string, string>(role, node) }, UiContext.User());

        OpResult OpAdd(int no) => ctx.Ops.ScriptAdd(no, UiContext.User());

        static string AddedScript(OpResult r) => r != null && r.Ok ? (r.Value as ScriptSetResult)?.Script : null;

        OpResult OpImport(string path) => ctx.Ops.ScriptImport(path, false, false, false, UiContext.User());

        static int? ImportedRow(OpResult r) => (r?.Value as ScriptImportResult)?.No;

        OpResult OpDelete(int no, bool restore) => ctx.Ops.ScriptStoreDelete(no, restore);

        /// <summary>The store's rows (hidden ones with <paramref name="deleted"/>) whose name or number holds <paramref name="q"/>; <paramref name="unreadable"/> when the index could not be read.</summary>
        List<ScriptStoreRow> OpStoreRows(string q, bool deleted, out bool unreadable)
        {
            var r = ctx.Ops.ScriptStoreList(q, deleted);
            unreadable = r.Warnings.Count > 0;
            return (r.Value as ScriptStoreListResult)?.Rows ?? new List<ScriptStoreRow>();
        }

        ScriptPurgeResult OpPurgePreview(out bool unreadable)
        {
            var p = ctx.Ops.ScriptPurgePreview().Value as ScriptPurgeResult ?? new ScriptPurgeResult();
            unreadable = p.Unreadable;
            return p;
        }

        ScriptPurgeResult OpPurge(out OpResult r)
        {
            r = ctx.Ops.ScriptPurge();
            return r.Value as ScriptPurgeResult;
        }
    }
}
