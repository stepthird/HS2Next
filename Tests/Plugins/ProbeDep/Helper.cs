// A private dependency of the probe plugin (CC0-1.0): the loader resolves it from the plugin's lib folder.
namespace Stp3.Test.ProbeDep
{
    public static class Helper
    {
        public static string Hello() => "dep ok";
        public static int Twice(int n) => n * 2;
    }
}
