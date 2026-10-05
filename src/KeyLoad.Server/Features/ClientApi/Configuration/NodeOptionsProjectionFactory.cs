using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Retains the exact validated nested node snapshot as native options for its execution owner.</summary>
internal sealed class NodeOptionsProjectionFactory<T>(IOptions<NodeOptions> node, Func<NodeOptions, T> select)
    : IOptionsFactory<T> where T : class
{
    public T Create(string name) => select(node.Value);
}
