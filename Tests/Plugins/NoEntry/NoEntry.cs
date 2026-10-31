// A test plugin assembly of the loader (CC0-1.0) without an IPlugin type: the loader refuses it with plugin.noEntry.
namespace Stp3.Test.NoEntry
{
    public static class NotAPlugin
    {
        public static int Answer() => 42;
    }
}
