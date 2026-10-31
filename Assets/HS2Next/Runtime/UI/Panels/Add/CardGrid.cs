// A virtualised thumbnail grid of files (Spike S4b): since 4.1 a
// ThumbGrid (Runtime/UI/Panels/Inspector/Edit/Widgets/ThumbGrid.cs) whose images come from a
// ThumbnailLoader of files. Rows hold N cells (N from the width and the zoom), bound to an index list (ListIndex.Indices)
// over entries; each cell asks the loader on bind and releases on unbind, shows the placeholder while loading and the
// error tile for undecodable files. Selection is local to the grid (one entry, click; double click / Enter = activate).
// The cell text is the entry's display name. Used by the character, pose, scene and image browsers. Card images are
// drawn, never inspected.
using System;

namespace HS2Next.UI.Panels
{
    public sealed class CardGrid<T> : ThumbGrid<T> where T : class
    {
        public const float BaseWidth = 126f, BaseHeight = 176f, TextHeight = 18f;

        public CardGrid(string name, ThumbnailLoader loader, Func<T, string> imagePath, Func<T, string> text, Func<T, bool> hasImage = null, float aspect = BaseHeight / BaseWidth)
            : base(name, new FileThumbSource<T>(loader, imagePath, hasImage), text, BaseWidth, aspect, TextHeight)
        {
        }
    }
}
