// Registration of the HS2 card importer for scenes: sets CharaImport.Importer (the
// 2.2 hook, a process-wide static) to the importer over the services' catalog. Called once by SceneRuntime.CreateDefault
// (Editor play mode, player, the 2.4 bridge bootstrap) and by tests that want a specific importer, which dispose the
// handle in finally. In a Unity runtime an importer is always available (Phase 4.6): ImporterOf gives the registered one,
// else the catalog's, so adding or changing a character from a card always imports it. The catalog's importer writes
// the build's version into the provenance (`import.importer` = "<Application.version>+imp.<revision>") and renders no
// thumbnails (an import per card add must not build twice; standalone file imports pass ImportThumbnails). Idempotent
// for the same catalog: the importer is created once per catalog. One catalog per application is assumed.
using System;
using HS2Next.Content;
using HS2Next.Import;

namespace HS2Next.Scenes.Runtime
{
    public static class ImportSetup
    {
        /// <summary>Creates the importer of a catalog (package A's Hs2Importer); replaceable by tests (e.g. an importer that throws).</summary>
        public static Func<ContentCatalog, ICharaImporter> Factory = DefaultFactory;

        static ICharaImporter DefaultFactory(ContentCatalog catalog) =>
            new Hs2Importer(catalog, new ImportOptions { AppVersion = AppSaveOptions.Version });

        static ContentCatalog cachedFor;
        static ICharaImporter cached;
        static Func<ContentCatalog, ICharaImporter> cachedFactory;

        /// <summary>The importer of <paramref name="catalog"/> (created on first use per catalog and factory).</summary>
        public static ICharaImporter ImporterFor(ContentCatalog catalog)
        {
            if (cached == null || !ReferenceEquals(cachedFor, catalog) || cachedFactory != Factory)
            {
                cached = Factory?.Invoke(catalog);
                cachedFor = catalog;
                cachedFactory = Factory;
            }
            return cached;
        }

        /// <summary>
        /// The importer a Unity runtime uses: the registered one (<see cref="CharaImport.Importer"/>), else the importer of the
        /// services' catalog; null only without both (no catalog).
        /// </summary>
        public static ICharaImporter ImporterOf(RuntimeServices services) =>
            CharaImport.Importer ?? (services?.Catalog != null ? ImporterFor(services.Catalog) : null);

        /// <summary>Sets <see cref="CharaImport.Importer"/> to the importer of <paramref name="services"/>' catalog; the handle restores the previous value.</summary>
        public static IDisposable Register(RuntimeServices services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            var prev = CharaImport.Importer;
            CharaImport.Importer = ImporterFor(services.Catalog);
            return new Restore(prev);
        }

        /// <summary>Drops the cached importer (after a catalog reload).</summary>
        public static void Reset()
        {
            cached = null;
            cachedFor = null;
            cachedFactory = null;
        }

        sealed class Restore : IDisposable
        {
            readonly ICharaImporter prev;
            bool done;
            public Restore(ICharaImporter prev) { this.prev = prev; }
            public void Dispose()
            {
                if (done) return;
                done = true;
                CharaImport.Importer = prev;
            }
        }
    }
}
