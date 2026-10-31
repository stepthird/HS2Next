// Imports *.tsv files as TextAssets: Unity's DefaultImporter does not make a TextAsset of
// the .tsv extension, so Resources.Load<TextAsset>("UI/L10n/strings") would be null in the player (P's question in the
// 3.5 coordination file). The text is read as UTF-8 (a BOM is kept; L10nTable.Parse skips it).
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

[ScriptedImporter(1, "tsv")]
public sealed class UiTsvImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var ta = new TextAsset(File.ReadAllText(ctx.assetPath, System.Text.Encoding.UTF8)) { name = Path.GetFileNameWithoutExtension(ctx.assetPath) };
        ctx.AddObjectToAsset("text", ta);
        ctx.SetMainObject(ta);
    }
}
