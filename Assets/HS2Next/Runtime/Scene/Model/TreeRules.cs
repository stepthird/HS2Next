// Tree rule switches of the scene model. The defaults are vanilla.
namespace HS2Next.Scenes
{
    public sealed class TreeRules
    {
        /// <summary>Allow route nodes to be re-parented (vanilla: no; open point O6). Reordering among siblings is always allowed.</summary>
        public bool AllowRouteReparent;
    }
}
