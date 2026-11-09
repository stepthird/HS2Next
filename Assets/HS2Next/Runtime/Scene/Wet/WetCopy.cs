// A character's block on a copy of its node (duplicate, a scene imported): the stains and their maps come along, with the same pictures, at the moment they show, not
// running; puddles stay behind, as they lie on the floor and not on the body.
using HS2Next.Save;

namespace HS2Next.Scenes.Wet
{
    public static class WetCopy
    {
        /// <summary>Makes <paramref name="block"/>, already a copy, what a copied node holds.</summary>
        public static void Prepare(CharaWet block)
        {
            block.Puddles.Clear();
            foreach (var s in block.Stains) s.Running = false;
        }
    }
}
