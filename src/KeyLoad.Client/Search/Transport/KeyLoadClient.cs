using ManagedCode.Communication;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Runs a bounded nearest-neighbor or hybrid search.</summary>
    /// <param name="request">Search collection, vector/text query and limits.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The ranked documents.</returns>
    public Task<Result<RankedDocument[]>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        => Send<RankedDocument[]>(ApiRoutes.Search, request, false, null, cancellationToken);
}
