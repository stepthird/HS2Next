// Item rows (engine side of the plugin Api; experimental: they may change in any minor release): a plugin adds item groups to the
// Studio list whose rows it makes itself. Each row's object comes from the plugin's function when an item of that row is built: in a
// scene that is added, loaded, duplicated or built again. Rows appear in the item list and in item.search, and an item of a row saves
// as any item, by "<guid>#<id>", its group and its category. When the plugin is not there, such an item loads as the fallback item
// and its saved data is kept as it is.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Api.Unity
{
    /// <summary>Item groups of the Studio list whose rows the plugin makes. Main thread only. Implemented by the host.</summary>
    [Experimental("generated item rows may change in any minor release")]
    public interface IItemRows
    {
        /// <summary>
        /// Adds an item group whose rows' objects come from <paramref name="make"/>. The group starts with no category and no row:
        /// name its categories with <see cref="IItemRowGroup.SetCategory"/> and give its rows with <see cref="IItemRowGroup.SetRows"/>.
        /// Every row's key is "&lt;this plugin's GUID&gt;#&lt;id&gt;". The scene's items of the group's rows that loaded as the
        /// fallback item before are built again.
        /// <para>
        /// <paramref name="make"/> runs on the main thread inside an item's build and returns a new object for each call, or null. The
        /// host takes the object over: places it under the item at local identity, copies every material slot and maps the shaders
        /// as for an item of the lists, and destroys it when the item goes. It finds the row's FK bones by name in the object. A
        /// renderer named as a mosaic member or with a mosaic material is a member; <see cref="IUnityPluginServices.AddMosaic"/> adds
        /// others. A throw or null loads the fallback item with the warning item.generatorFailed and counts as a fault of the plugin.
        /// <paramref name="make"/> does not add scene nodes or change the scene (warning item.generatorWrote).
        /// </para>
        /// <para>The group goes when it is disposed, and when the plugin unloads, is disabled or faults.</para>
        /// </summary>
        /// <param name="group">The group number: one number space with the groups of the item lists.</param>
        /// <param name="name">The group's name in the item list.</param>
        /// <param name="make">Makes a row's object for one item.</param>
        /// <returns>The group.</returns>
        /// <exception cref="ArgumentException">The number is used by the item lists, the host or another plugin.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="make"/> is null.</exception>
        IItemRowGroup AddGroup(int group, string name, Func<ItemRowRequest, GameObject> make);
    }

    /// <summary>
    /// One item group of the plugin. Dispose removes the group and its rows; items already built keep their objects until they are
    /// built again, then they load as the fallback item. Implemented by the host.
    /// </summary>
    [Experimental("generated item rows may change in any minor release")]
    public interface IItemRowGroup : IDisposable
    {
        /// <summary>The group number.</summary>
        int Group { get; }

        /// <summary>Names a category of the group. A category is listed while it holds rows.</summary>
        /// <param name="category">The category number.</param>
        /// <param name="name">Its name in the item list.</param>
        void SetCategory(int category, string name);

        /// <summary>
        /// Replaces every row of the group. Ids are unique in the group. The scene's items of rows that are new, or whose name, category
        /// or FK bones changed, are built again at the next frame; an item of a row that is gone keeps its object until it is built
        /// again.
        /// </summary>
        /// <param name="rows">The rows.</param>
        /// <exception cref="ArgumentException">Two rows have one id.</exception>
        void SetRows(IEnumerable<ItemRow> rows);

        /// <summary>The scene's items of row <paramref name="id"/> are built again at the next frame (<c>make</c> runs for each).</summary>
        /// <param name="id">The row's id.</param>
        void Refresh(int id);
    }

    /// <summary>One row of a plugin's item group.</summary>
    [Experimental("generated item rows may change in any minor release")]
    public sealed class ItemRow
    {
        /// <summary>The category number.</summary>
        public int Category;
        /// <summary>The id, unique in the group; the key is "&lt;plugin GUID&gt;#&lt;id&gt;".</summary>
        public int Id;
        /// <summary>The name in the item list.</summary>
        public string Name;
        /// <summary>The names of the transforms of the row's object that item FK moves; null: none.</summary>
        public IReadOnlyList<string> FkBones;
    }

    /// <summary>What <c>make</c> is asked for: the row of one item build. Made by the host.</summary>
    [Experimental("generated item rows may change in any minor release")]
    public sealed class ItemRowRequest
    {
        /// <summary>Made by the host.</summary>
        /// <param name="guid">The GUID of the row's key.</param>
        /// <param name="group">The group number.</param>
        /// <param name="category">The row's category.</param>
        /// <param name="id">The row's id.</param>
        /// <param name="nodeId">The scene node being built.</param>
        public ItemRowRequest(string guid, int group, int category, int id, string nodeId)
        {
            Guid = guid;
            Group = group;
            Category = category;
            Id = id;
            NodeId = nodeId;
        }

        /// <summary>The GUID of the row's key (the plugin's).</summary>
        public string Guid { get; }
        /// <summary>The group number.</summary>
        public int Group { get; }
        /// <summary>The row's category.</summary>
        public int Category { get; }
        /// <summary>The row's id.</summary>
        public int Id { get; }
        /// <summary>The scene node being built.</summary>
        public string NodeId { get; }
    }
}
