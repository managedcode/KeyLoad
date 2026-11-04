using ManagedCode.Communication;
using Microsoft.Extensions.VectorData;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Runs a bounded nearest-neighbor or hybrid search.</summary>
    /// <param name="request">Search collection, vector/text query and limits.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The ranked documents.</returns>
    public Task<Result<RankedDocument[]>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        => Send<RankedDocument[], SearchRequest>(ApiRoutes.Search, request, false, null, cancellationToken);

    /// <summary>Exposes exact vector search over one existing KeyLoad collection and field.</summary>
    /// <param name="partition">The partition containing the collection.</param>
    /// <param name="collection">The collection to search.</param>
    /// <param name="vectorField">The KeyLoad vector field pointer.</param>
    /// <param name="space">The configured embedding model, dimension and metric.</param>
    /// <returns>A VectorData search source using this client's credentials and transport.</returns>
    public IVectorSearchable<DocumentResult> AsVectorSearchable(PartitionRef partition, string collection,
        string vectorField, VectorSpace space)
    {
        ArgumentNullException.ThrowIfNull(partition) /* KLD0041: BCL null validation accepts the PartitionRef through its object parameter. */;
        ArgumentNullException.ThrowIfNull(space) /* KLD0041: BCL null validation accepts the VectorSpace through its object parameter. */;
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(vectorField);
        return new KeyLoadVectorSearchable(this, partition, collection, vectorField, space);
    }
}
