// A test plugin of the loader (CC0-1.0) whose public types derive from a type of an assembly that is not shipped next to it:
// listing its types fails, and the loader refuses it with plugin.typeLoad.
using HS2Next.Api.Plugins;

namespace Stp3.Test.TypeLoad
{
    public sealed class Derived : Stp3.Test.TypeLoadMissing.MissingBase
    {
        public override int Value => 1;
    }

    public sealed class TypeLoadPlugin : IPlugin
    {
        public void Load(IPluginContext context) { }
        public void Unload() { }
    }
}
