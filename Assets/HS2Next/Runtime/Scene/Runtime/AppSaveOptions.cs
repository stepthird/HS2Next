// The one source of the save options every save of the app completes (Phase 4.6): scene saves
// (SceneOps), character and coordinate saves (StudioOps), pose fragments (PoseFragmentOps) and the standalone imports
// (CharaFileImport / CoordFileImport, whose callers pass completed options). It fills what the caller left open:
//   app.version  = Application.version (the build's version; PlayerSettings.bundleVersion);
//   requires     = the installed zipmod manifest's name / author / website of each GUID, read through the importer of the
//                  services' catalog (null for a GUID no installed zipmod declares: the file's own details or nulls stay);
//   clock        = left as given (null = now; tests and the bridge pass a fixed one).
// The Unity-free default SaveFormats.AppVersion stays for plain-Mono code and the test fixtures.
using System;
using HS2Next.Import;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public static class AppSaveOptions
    {
        /// <summary>
        /// Returns <paramref name="o"/> (a new one when null) with <see cref="SaveOptions.AppVersion"/> = the build's version
        /// and <see cref="SaveOptions.RequiresInfo"/> = the installed zipmods' manifest details when the caller set none;
        /// <see cref="SaveOptions.Clock"/> is left as given.
        /// </summary>
        public static SaveOptions Complete(SaveOptions o, RuntimeServices services)
        {
            o ??= new SaveOptions();
            if (o.AppVersion == null) o.AppVersion = Version;
            if (o.RequiresInfo == null)
            {
                var importer = ImportSetup.ImporterOf(services) as Hs2Importer;
                if (importer != null) o.RequiresInfo = guid => Details(importer, guid);
            }
            return o;
        }

        /// <summary>The version written to `app.version` (Application.version; the Unity-free default when it is empty).</summary>
        public static string Version
        {
            get
            {
                string v = Application.version;
                return string.IsNullOrEmpty(v) ? SaveFormats.AppVersion : v;
            }
        }

        static RequiresEntry Details(Hs2Importer importer, string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            try { return importer.RequiresInfo(guid); }
            catch (Exception) { return null; }   // a catalog that cannot answer keeps the file's details
        }
    }
}
