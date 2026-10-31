// TestCharaBuild: a test's character built the way the app builds one - the card imported by the catalog's importer into
// documents (ImportSetup.ImporterFor), the build input from the documents (CharaDocumentInput.From), then
// CharaAssembler.Build(input), then CharaDocumentInput.AfterBuild (the missing content the document resolver met joins
// Context.Missing, as the scene's builder does). Tests call asm.BuildFromDocuments(card) where they built from a card
// before; the result, the assembler's Context and its log are the document build's. Nothing here is a shortcut of the
// app's path.
using System;
using System.IO;
using HS2Next.Chara;
using HS2Next.Chara.Documents;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;

public static class TestCharaBuild
{
    /// <summary>The import and adapter findings of the last <see cref="BuildFromDocuments(CharaAssembler, Card)"/>.</summary>
    public static FindingList LastFindings { get; private set; } = new FindingList();

    /// <summary>The documents of <paramref name="card"/> (read from its file) built by <paramref name="asm"/>.</summary>
    public static GameObject BuildFromDocuments(this CharaAssembler asm, Card card)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (string.IsNullOrEmpty(card.Path) || !File.Exists(card.Path)) throw new FileNotFoundException("the card's file is needed for the import", card.Path);
        return asm.BuildFromDocuments(File.ReadAllBytes(card.Path), card.Path);
    }

    /// <summary>The documents of the card file <paramref name="cardPath"/> built by <paramref name="asm"/>.</summary>
    public static GameObject BuildFromDocuments(this CharaAssembler asm, string cardPath) =>
        asm.BuildFromDocuments(File.ReadAllBytes(cardPath), cardPath);

    /// <summary>The documents of the card file <paramref name="cardPath"/>, changed by <paramref name="edit"/> after the import
    /// (as a user's edit would change them), built by <paramref name="asm"/>.</summary>
    public static GameObject BuildFromDocuments(this CharaAssembler asm, string cardPath, Action<CharaDocument, CoordDocument> edit) =>
        asm.BuildFromDocuments(File.ReadAllBytes(cardPath), cardPath, edit);

    /// <summary>The documents of the card bytes <paramref name="bytes"/> (<paramref name="path"/> names them), optionally changed by
    /// <paramref name="edit"/> after the import, built by <paramref name="asm"/>.</summary>
    public static GameObject BuildFromDocuments(this CharaAssembler asm, byte[] bytes, string path, Action<CharaDocument, CoordDocument> edit = null)
    {
        var catalog = asm.Catalog ?? throw new InvalidOperationException("the assembler has no catalog");
        var findings = new FindingList();
        LastFindings = findings;
        var src = new CardAppearanceSource { FullPath = path, Sha256 = ResStore.Sha256Hex(bytes), ReadBytes = () => bytes, Name = Path.GetFileNameWithoutExtension(path) };
        var imported = ImportSetup.ImporterFor(catalog).Import(src, findings) ?? throw new InvalidOperationException("the importer returned nothing for " + path);
        edit?.Invoke(imported.Subtree.Chara, imported.Subtree.Coord);
        var input = CharaDocumentInput.From(imported.Subtree, imported.Res, catalog, findings);
        var root = asm.Build(input);
        CharaDocumentInput.AfterBuild(asm.Context);
        return root;
    }
}
