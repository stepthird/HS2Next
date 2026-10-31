// Default tree labels of nodes without a stored name. The Unity layer
// injects a provider that knows the list row names of items and lights; the headless default uses what the
// model has: a character's profile or card name, a node's own name, else "<kind> <first 8 hex of the id>".
namespace HS2Next.Scenes
{
    public interface ILabelProvider
    {
        /// <summary>The label of a node whose <c>Doc.Name</c> is null. Never null.</summary>
        string DefaultLabel(TreeNode node);
    }

    public class DefaultLabelProvider : ILabelProvider
    {
        public static readonly DefaultLabelProvider Instance = new DefaultLabelProvider();

        public virtual string DefaultLabel(TreeNode node)
        {
            if (node == null) return "";
            if (node.Kind == NodeKind.Chara)
            {
                var a = node.Appearance;
                string n = a?.Stored?.Chara?.Profile?.Name;
                if (string.IsNullOrEmpty(n)) n = a?.Card?.Name;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            else if (!string.IsNullOrEmpty(node.Doc?.Name)) return node.Doc.Name;
            return Fallback(node);
        }

        /// <summary>"&lt;kind&gt; &lt;first 8 hex of the id&gt;".</summary>
        public static string Fallback(TreeNode node)
        {
            string kind = node.Kind == NodeKind.Opaque ? node.Doc?.Kind ?? "node" : node.Doc?.Kind ?? node.Kind.ToString().ToLowerInvariant();
            string id = node.Id ?? "";
            return kind + " " + (id.Length > 8 ? id.Substring(0, 8) : id);
        }
    }
}
