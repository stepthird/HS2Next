# Shapes: rows of the Studio item list made by a plugin

This sample (CC0-1.0) adds an item group to the Studio item list: one category, five rows (Cube, Sphere, Cylinder, Torus,
Arrow). Their objects are built in code whenever an item of a row is built, so the plugin ships no asset. The torus is a
skinned mesh with eight bones along its ring; its row names them as FK bones, so item FK bends it. The API is experimental
(`[Experimental]`) and may change in any minor release; `PLUGINS.md`, "Item rows", describes it.

## Build and try it

1. In the `PluginKit` folder run `dotnet build`. The sample is copied into `out/stp3.sample.shapes/`.
2. Copy that folder into the `Plugins` folder next to `HS2Next.exe` and start HS2Next.
3. The item list shows the group "Shapes". Add the Torus and bend it with item FK as any item: turning `ring2` turns the rest
   of the ring with it. Through the bridge: `item.add {"ref": "stp3.sample.shapes#4"}`, then
   `item.set {"key": "<id>", "set": {"fk.enabled": true, "fk.bones[\"ring2\"]": [0, -45, 40]}}`.

## How it works

`ShapesPlugin.Load` asks for the item rows of the engine side and adds its group:

```csharp
group = ctx.Unity().ItemRows.AddGroup(Group, "Shapes", Make);
group.SetCategory(Category, "Shapes");
group.SetRows(new[] { new ItemRow { Category = Category, Id = Cube, Name = "Cube" }, /* ... */ });
```

- **Change the group number** (`ShapesPlugin.Group`, 820001) before you base a plugin of your own on this one. Group numbers are
  one space with the groups of the item lists: `AddGroup` throws `ArgumentException` when the lists, the app or another plugin
  use the number.
- **Rows.** A row is a category, an id (unique in the group), a name and, when item FK should move parts of the object, the names
  of those transforms (`FkBones`). An item of a row saves as `stp3.sample.shapes#<id>` with the group and the category.
- **Make** runs inside an item's build and returns a new object for each call (or null). The app owns the object from then on:
  it places it under the item, copies its materials per slot and maps their shaders, and destroys it when the item goes. Build
  the materials from shaders the app has (here `HS2Next/Standard`), or from your own bundle.
- **Change rows** with `SetRows` (the items of new or changed rows are built again at the next frame) and `Refresh(id)` (the
  items of one row are built again). `Dispose` removes the group; the app removes it too when the plugin unloads.
- **Without the plugin** a saved scene's items of these rows load as the fallback item and keep their data; with the plugin
  installed again they are the shapes again.
