// Core format migrations. A migration upgrades the raw
// JObjects of one file from formatVersion n to n + 1 before the documents are bound; migrations
// must be idempotent for members they add (they never overwrite an existing member). The
// registry holds exactly one migration per From version up to the supported revision S; a gap is a
// startup error. The registry of this build has S = 5 with the empty, silent migrations 1 → 2 (revision 2 only adds an
// optional member, the clothes parts' inline ext), 2 → 3 (after 1.1.0: a character node's state.nippleThroughClothes),
// 3 → 4 (after 1.2.0: the scene's timeline) and 4 → 5 (after 1.3.x: a character node's wet): files of the revisions 1 to 4 load
// exactly as before, with no finding.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    public interface ICoreMigration
    {
        /// <summary>Migrates From → From + 1.</summary>
        int From { get; }
        void Apply(MigrationContext ctx);
    }

    public sealed class MigrationContext
    {
        /// <summary>"hs2next.chara", "hs2next.coord" or "hs2next.scene".</summary>
        public string Format { get; }
        public JObject Manifest { get; }
        /// <summary>Entry name → document ("chara.json", "coord/coord.json", "charas/&lt;id&gt;/chara.json", …).</summary>
        public IReadOnlyDictionary<string, JObject> Documents { get; }
        readonly FindingList findings;
        readonly int from;

        public MigrationContext(string format, JObject manifest, IReadOnlyDictionary<string, JObject> documents, FindingList findings, int from)
        {
            Format = format;
            Manifest = manifest;
            Documents = documents;
            this.findings = findings;
            this.from = from;
        }

        public void Report(string code, string message) =>
            findings.Info("migrate.core." + from + "." + code, "", message);
    }

    public sealed class CoreMigrationRegistry
    {
        /// <summary>The registry of this build: supported revision <see cref="SaveFormats.SupportedVersion"/> with the empty,
        /// silent migrations 1 → 2, 2 → 3, 3 → 4 and 4 → 5.</summary>
        public static readonly CoreMigrationRegistry Default = new CoreMigrationRegistry(SaveFormats.SupportedVersion, Empty(1), Empty(2), Empty(3), Empty(4));

        /// <summary>A migration From → From + 1 that changes nothing and reports nothing (a revision that only adds optional
        /// members); the registry adds no <c>migrate.core</c> info for it.</summary>
        public static ICoreMigration Empty(int from) => new EmptyMigration(from);

        sealed class EmptyMigration : ICoreMigration
        {
            public EmptyMigration(int from) { From = from; }
            public int From { get; }
            public void Apply(MigrationContext ctx) { }
        }

        readonly SortedDictionary<int, ICoreMigration> byFrom = new SortedDictionary<int, ICoreMigration>();

        /// <summary>The supported revision S: files up to it are read (older ones migrated), newer ones bound with NewerFormat.</summary>
        public int Supported { get; }

        /// <summary>
        /// A registry for revision <paramref name="supported"/> with the given migrations: exactly one per From in
        /// 1 … supported − 1 (a gap or a duplicate throws InvalidOperationException).
        /// </summary>
        public CoreMigrationRegistry(int supported, params ICoreMigration[] migrations)
        {
            if (supported < 1) throw new ArgumentOutOfRangeException(nameof(supported));
            Supported = supported;
            foreach (var m in migrations)
            {
                if (m.From < 1 || m.From >= supported) throw new InvalidOperationException("migration From " + m.From + " outside 1.." + (supported - 1));
                if (byFrom.ContainsKey(m.From)) throw new InvalidOperationException("two migrations From " + m.From);
                byFrom[m.From] = m;
            }
            for (int v = 1; v < supported; v++)
                if (!byFrom.ContainsKey(v)) throw new InvalidOperationException("no core migration From " + v + " (gap)");
        }

        /// <summary>Runs the migrations formatVersion … S − 1 in order on the raw documents.</summary>
        public void Upgrade(int formatVersion, MigrationContext ctxTemplate, FindingList findings)
        {
            for (int v = Math.Max(1, formatVersion); v < Supported; v++)
            {
                var m = byFrom[v];
                if (m is EmptyMigration) continue;      // silent: nothing changes, nothing is reported
                var ctx = new MigrationContext(ctxTemplate.Format, ctxTemplate.Manifest, ctxTemplate.Documents, findings, v);
                m.Apply(ctx);
                findings.Info("migrate.core", "", "format " + v + " → " + (v + 1));
            }
        }
    }
}
