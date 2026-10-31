// UncensorSelector (`com.deathweasel.bepinex.uncensorselector`). Character cards only.
using HS2Next.IO;

namespace HS2Next.Data.Ext
{
    public class UncensorData
    {
        public int Version;
        /// <summary>Uncensor GUIDs from zipmod manifests (`&lt;KK_UncensorSelector&gt;`); null = use the configured default (blank is read as null).</summary>
        public string BodyGUID, PenisGUID, BallsGUID;
        /// <summary>Informational only: on load HS2 derives it (male true, female = parameter.futanari). Null when absent.</summary>
        public bool? DisplayPenis;
        /// <summary>Balls renderer enabled; null when absent (male default true, female default false).</summary>
        public bool? DisplayBalls;
        /// <summary>Version 1 data: the combined uncensor GUID (migrated through manifest &lt;migration&gt; entries).</summary>
        public string LegacyUncensorGUID;

        public static UncensorData Decode(int version, object data, ExtContext ctx)
        {
            var r = new UncensorData
            {
                Version = version,
                BodyGUID = V.SN(data.Get("BodyGUID")), PenisGUID = V.SN(data.Get("PenisGUID")), BallsGUID = V.SN(data.Get("BallsGUID")),
                DisplayPenis = V.BN(data.Get("DisplayPenis")), DisplayBalls = V.BN(data.Get("DisplayBalls")),
            };
            if (version == 1) r.LegacyUncensorGUID = V.SN(data.Get("UncensorGUID"));
            else if (version != 2) ctx.Warn(PluginIds.Uncensor, $"unknown version {version}");
            return r;
        }
    }

    /// <summary>Invisible Body (`KK_InvisibleBody`). Character cards only.</summary>
    public class InvisibleBodyData
    {
        public int Version;
        /// <summary>false = hide body, head and hair renderers (clothes and accessories stay).</summary>
        public bool Visible = true;

        public static InvisibleBodyData Decode(int version, object data, ExtContext ctx)
        {
            if (version != 0) ctx.Warn(PluginIds.InvisibleBody, $"unknown version {version}");
            return new InvisibleBodyData { Version = version, Visible = V.B(data.Get("Visible"), true) };
        }
    }
}
