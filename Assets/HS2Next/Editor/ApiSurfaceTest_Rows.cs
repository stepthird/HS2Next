// ApiSurfaceTest, group AZ8: the generated item rows in the contract. IItemRows, IItemRowGroup, ItemRow, ItemRowRequest and
// IUnityPluginServices.ItemRows are on the listing and experimental, the two interfaces are implemented by the host (a delegate makes
// the rows' objects, no interface of a plugin is added), and the request is made by the host with read-only members. What the rows
// do is BakeRowsTest (BR1-BR10).
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next.Api.Unity;

public static partial class ApiSurfaceTest
{
    const string RowsPhrase = "generated item rows may change in any minor release";

    static void Az8()
    {
        foreach (var t in new[] { typeof(IItemRows), typeof(IItemRowGroup), typeof(ItemRow), typeof(ItemRowRequest) })
            Check("AZ8", t.Name + " is experimental", ExperimentalReason(t) == RowsPhrase, ExperimentalReason(t));
        var prop = typeof(IUnityPluginServices).GetProperty(nameof(IUnityPluginServices.ItemRows));
        Check("AZ8", "IUnityPluginServices.ItemRows is experimental", prop != null && ExperimentalReason(prop) == RowsPhrase, prop == null ? "no such member" : ExperimentalReason(prop));
        Check("AZ8", "IItemRows and IItemRowGroup are implemented by the host", !PluginImplemented.Contains(typeof(IItemRows).FullName) && !PluginImplemented.Contains(typeof(IItemRowGroup).FullName),
            string.Join(", ", PluginImplemented));
        Check("AZ8", "IItemRows is one member, AddGroup with a delegate", typeof(IItemRows).GetMembers().Select(m => m.Name).SequenceEqual(new[] { "AddGroup" })
            && typeof(IItemRows).GetMethod("AddGroup").GetParameters().Last().ParameterType == typeof(Func<ItemRowRequest, UnityEngine.GameObject>),
            string.Join(", ", typeof(IItemRows).GetMembers().Select(m => m.Name)));
        Check("AZ8", "IItemRowGroup is disposable", typeof(IDisposable).IsAssignableFrom(typeof(IItemRowGroup)), "");
        var req = typeof(ItemRowRequest).GetProperties();
        Check("AZ8", "ItemRowRequest's members are read-only", req.Length == 5 && req.All(p => p.CanRead && !p.CanWrite), string.Join(", ", req.Select(p => p.Name + (p.CanWrite ? " (writable)" : ""))));

        string unity = File.ReadAllText(Path.Combine(ListingDir, ApiUnityFile), Encoding.UTF8).Replace("\r\n", "\n");
        var lines = unity.Split('\n').Where(l => l.StartsWith("T HS2Next.Api.Unity.IItemRow", StringComparison.Ordinal) || l.StartsWith("T HS2Next.Api.Unity.ItemRow", StringComparison.Ordinal)
            || l.StartsWith("M HS2Next.Api.Unity.IUnityPluginServices::ItemRows ", StringComparison.Ordinal)).ToList();
        Check("AZ8", "the listing holds the four types and IUnityPluginServices.ItemRows, all experimental", lines.Count == 5 && lines.All(l => l.EndsWith("[Experimental]", StringComparison.Ordinal)),
            string.Join(" / ", lines));
    }
}
