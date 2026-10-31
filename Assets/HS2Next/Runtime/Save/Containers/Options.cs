// Load and save options and results of the containers (
// 5).
using System;
using System.IO;

namespace HS2Next.Save
{
    public enum ThumbnailMode
    {
        /// <summary>Render a new thumbnail through <see cref="SaveOptions.Renderer"/>; without one keep the previous bytes, else a placeholder.</summary>
        Render,
        /// <summary>Keep the previous thumbnail bytes (a placeholder when there are none).</summary>
        Keep,
        /// <summary>Write the solid grey placeholder.</summary>
        Placeholder,
    }

    public sealed class LoadOptions
    {
        /// <summary>Type errors in known members become error findings and the "absent" value is used.</summary>
        public bool Lenient;
        /// <summary>A required ext block without a working handler fails the load (default: load with a warning).</summary>
        public bool FailOnMissingRequiredExt;
        /// <summary>Reserved. v1 always materialises preserved bytes at load.</summary>
        public bool LazyPreservedBytes;
        /// <summary>Ext handlers to dispatch to; null = <see cref="ExtRegistry.Default"/>.</summary>
        public ExtRegistry Registry;
        /// <summary>Core migrations and the supported revision; null = <see cref="CoreMigrationRegistry.Default"/>.</summary>
        public CoreMigrationRegistry Migrations;

        internal ExtRegistry RegistryOrDefault => Registry ?? ExtRegistry.Default;
        internal CoreMigrationRegistry MigrationsOrDefault => Migrations ?? CoreMigrationRegistry.Default;
    }

    public sealed class SaveOptions
    {
        /// <summary>Remove every `role: source` entry of `hs2.kkex` from the written file. The in-memory copy keeps them.</summary>
        public bool StripImportSources;
        /// <summary>Re-open the written tmp file and check the end record, central directory and the documents' CRC before replacing the target.</summary>
        public bool Verify = true;
        public ThumbnailMode Thumbnail = ThumbnailMode.Render;
        /// <summary>Renders a 504 × 704 PNG for <see cref="ThumbnailMode.Render"/> (Unity side: ThumbnailCapture); null = none available.</summary>
        public Func<byte[]> Renderer;
        /// <summary>Time source for createdUtc/modifiedUtc (tests use a fixed clock); null = DateTime.UtcNow.</summary>
        public Func<DateTime> Clock;
        /// <summary>Ext handlers offered the model; null = <see cref="ExtRegistry.Default"/>.</summary>
        public ExtRegistry Registry;
        /// <summary>`app.version` to write; null = <see cref="SaveFormats.AppVersion"/>.</summary>
        public string AppVersion;
        /// <summary>Name/author/website for a `requires` GUID (the content catalog); null or a null result = keep what the file had, else nulls.</summary>
        public Func<string, RequiresEntry> RequiresInfo;
        /// <summary>Tests only: wraps the file stream of the tmp file (fault injection).</summary>
        public Func<Stream, Stream> StreamWrapper;

        internal ExtRegistry RegistryOrDefault => Registry ?? ExtRegistry.Default;
        internal DateTime Now => JsonText.TruncateToSeconds(Clock != null ? Clock() : DateTime.UtcNow);
    }

    /// <summary>Outcome of a load: the container (null on failure), the error, findings and the version flags.</summary>
    public sealed class LoadResult<T> where T : class
    {
        public T Value;
        /// <summary>Null on success.</summary>
        public SaveFormatException Error;
        public FindingList Findings = new FindingList();
        /// <summary>The file's formatVersion is newer than this reader; unknown members were kept.</summary>
        public bool NewerFormat;
        /// <summary>An unknown ext block marked required is present; the UI must warn before showing the content.</summary>
        public bool MissingRequiredExt;

        public bool Ok => Error == null;
        public SaveErrorCode? ErrorCode => Error?.Code;

        /// <summary>The value, or the error thrown.</summary>
        public T Unwrap() => Error == null ? Value : throw Error;

        public override string ToString() => Ok ? "ok (" + Findings.Count + " findings)" : Error.Message;
    }

    public sealed class SaveResult
    {
        /// <summary>Null on success (the target is then replaced); on failure the target is unchanged.</summary>
        public SaveFormatException Error;
        public FindingList Findings = new FindingList();
        /// <summary>The content was loaded from a newer formatVersion and written with this app's version.</summary>
        public bool Downgraded;
        /// <summary>Bytes of the written file.</summary>
        public long Size;

        public bool Ok => Error == null;

        public void ThrowIfFailed()
        {
            if (Error != null) throw Error;
        }

        public override string ToString() => Ok ? "ok, " + Size + " bytes (" + Findings.Count + " findings)" : Error.Message;
    }
}
