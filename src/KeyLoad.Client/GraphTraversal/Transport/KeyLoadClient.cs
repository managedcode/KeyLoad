using ManagedCode.Communication;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Traverses a bounded graph from the requested starting nodes.</summary>
    /// <param name="request">Graph traversal start, direction and limits.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed graph traversal result.</returns>
    public Task<Result<GraphTraversal>> TraverseAsync(TraverseRequest request, CancellationToken cancellationToken = default)
        => Send<GraphTraversal>(ApiRoutes.GraphTraverse, request, false, null, cancellationToken);
}
