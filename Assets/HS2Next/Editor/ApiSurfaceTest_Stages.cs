// ApiSurfaceTest, group AZ7: the character stages in the contract. ICharaStages and IUnityPluginServices.Stages are on the listing and
// experimental, the host implements the interface, and the plugin-implemented IMotionStage, with MotionPhase and MotionFrame, has the
// shape it was frozen with: a stage port adds a door, it changes no stage. What the port does on a character is BridgeCoreTest C48.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next.Api.Unity;
using HS2Next.Content;
using HS2Next.Motion;

public static partial class ApiSurfaceTest
{
    const string StagePhrase = "the character stages may change in any minor release";

    static readonly string[] MotionStageFrozen =
    {
        "M HS2Next.Motion.IMotionStage::Enabled | public System.Boolean Enabled { get; set; }",
        "M HS2Next.Motion.IMotionStage::Name | public System.String Name { get; }",
        "M HS2Next.Motion.IMotionStage::Phase | public HS2Next.Motion.MotionPhase Phase { get; }",
        "M HS2Next.Motion.IMotionStage::Reset() | public System.Void Reset()",
        "M HS2Next.Motion.IMotionStage::Step(ref HS2Next.Motion.MotionFrame) | public System.Void Step(in HS2Next.Motion.MotionFrame frame)",
    };

    static string ExperimentalReason(MemberInfo m) => m.GetCustomAttribute<HS2Next.Api.ExperimentalAttribute>()?.Note;

    static void Az7()
    {
        Check("AZ7", "ICharaStages is experimental", ExperimentalReason(typeof(ICharaStages)) == StagePhrase, ExperimentalReason(typeof(ICharaStages)));
        var prop = typeof(IUnityPluginServices).GetProperty(nameof(IUnityPluginServices.Stages));
        Check("AZ7", "IUnityPluginServices.Stages is experimental", prop != null && ExperimentalReason(prop) == StagePhrase, prop == null ? "no such member" : ExperimentalReason(prop));
        Check("AZ7", "ICharaStages is one member, host-implemented", typeof(ICharaStages).GetMembers().Length == 1 && !PluginImplemented.Contains(typeof(ICharaStages).FullName),
            string.Join(", ", typeof(ICharaStages).GetMembers().Select(x => x.Name)));

        string api = File.ReadAllText(Path.Combine(ListingDir, ApiFile), Encoding.UTF8).Replace("\r\n", "\n");
        string unity = File.ReadAllText(Path.Combine(ListingDir, ApiUnityFile), Encoding.UTF8).Replace("\r\n", "\n");
        var listed = api.Split('\n').Where(l => l.StartsWith("M HS2Next.Motion.IMotionStage::", StringComparison.Ordinal)).ToArray();
        Check("AZ7", "IMotionStage is listed with its 5 frozen members, implemented by plugins", listed.SequenceEqual(MotionStageFrozen) && api.Contains("T HS2Next.Motion.IMotionStage | interface implemented by plugins"), string.Join(" / ", listed));
        Check("AZ7", "MotionPhase keeps its 12 named phases", Enum.GetNames(typeof(MotionPhase)).Length == 12, string.Join(" ", Enum.GetNames(typeof(MotionPhase))));
        Check("AZ7", "the stage's types are not in the engine listing but for the port", unity.Contains("T HS2Next.Api.Unity.ICharaStages | interface [Experimental]") && !unity.Contains("IMotionStage::"), "");
        foreach (var f in new[] { "PLUGINS.md", "PLUGINS_ja.md", "PLUGINS_zh-Hans.md" })
        {
            string guide = File.ReadAllText(Path.Combine(ContentSettings.ProjectRoot, "Reference", f), Encoding.UTF8);
            var missing = new[] { "ctx.Unity().Stages", "ICharaStages", "Stages.Add(nodeId, root => new HeadWatch(root))", "<guid>/<Name>", "stage factory", "SceneLoaded", "-90", "940" }.Where(s => !guide.Contains(s)).ToArray();
            Check("AZ7", "Reference/" + f + " has the section on the character stages", missing.Length == 0, string.Join(", ", missing));
        }
    }
}
