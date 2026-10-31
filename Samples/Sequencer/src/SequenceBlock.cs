// Sample sequencer for HS2Next (CC0-1.0: dedicated to the public domain).
// The scene's ext block of the sequencer: the sequence is stored with the scene as the inline JSON value of the
// plugin's namespace (data version 1). Reading only parses and holds the sequence; the plugin adopts it when the scene
// has loaded. A newer data version or an invalid sequence is declined, so the host keeps the block unchanged and writes
// it back as it was.
using System;
using HS2Next.Api.Save;

namespace Stp3.Samples.Sequencer
{
    public sealed class SequenceBlock : IExtBlockHandler
    {
        readonly string ns;
        readonly Func<Sequence> current;
        readonly Action<Sequence> read;

        /// <param name="ns">The plugin GUID (the block's namespace).</param>
        /// <param name="current">The sequence to save (null: no block).</param>
        /// <param name="read">Receives a sequence read from a file.</param>
        public SequenceBlock(string ns, Func<Sequence> current, Action<Sequence> read)
        {
            this.ns = ns;
            this.current = current;
            this.read = read;
        }

        public string Namespace => ns;
        public int Version => Sequence.DataVersion;
        public ExtScope Scopes => ExtScope.Scene;
        public bool RequiredByDefault => false;

        public void Read(IExtReadContext ctx)
        {
            if (ctx.StoredVersion > Sequence.DataVersion)
            {
                ctx.Decline("sequence data version " + ctx.StoredVersion + " is newer than " + Sequence.DataVersion);
                return;
            }
            if (ctx.Json == null)
            {
                ctx.Report(ExtSeverity.Warning, "seq.invalid", "the sequence block has no inline value");
                ctx.Decline("no inline value");
                return;
            }
            if (!Sequence.TryParse(ctx.Json, out var seq, out string problem))
            {
                ctx.Report(ExtSeverity.Warning, "seq.invalid", "the stored sequence is not valid: " + problem);
                ctx.Decline(problem);
                return;
            }
            read(seq);
        }

        public void Write(IExtWriteContext ctx)
        {
            var seq = current();
            if (seq != null) ctx.SetJson(seq.ToJson());
        }
    }
}
