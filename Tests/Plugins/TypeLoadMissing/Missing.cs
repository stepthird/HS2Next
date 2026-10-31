// The dependency of the TypeLoad test plugin (CC0-1.0); it is never staged, so the plugin's types cannot load.
namespace Stp3.Test.TypeLoadMissing
{
    public abstract class MissingBase
    {
        public abstract int Value { get; }
    }
}
