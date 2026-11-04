using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.VectorData;

namespace KeyLoad.Client;

internal sealed class KeyLoadVectorSearchable(KeyLoadClient client, PartitionRef partition, string collection,
    string vectorField, VectorSpace space) : IVectorSearchable<DocumentResult>
{
    private const string SystemName = "keyload";
    private const string UnsupportedOptions = "KeyLoad vector search binds one field and returns ranked documents. " +
        "Filter, VectorProperty, IncludeVectors and ScoreThreshold are unsupported.";
    private const string UnsupportedEmbedding = "Supply a float array or ReadOnlyMemory<float> embedding.";
    private const string EmbeddingLengthMismatch = "The embedding length must match the configured vector space.";
    private const string SearchFailed = "KeyLoad vector search failed. Error code: ";

    public IAsyncEnumerable<VectorSearchResult<DocumentResult>> SearchAsync<TInput>(TInput searchValue, int top,
        VectorSearchOptions<DocumentResult>? options = null, CancellationToken cancellationToken = default)
        where TInput : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(top);
        if (options is { Filter: not null }
            or { VectorProperty /* KLD0041: VectorData supplies a field selector as object. Reject any selector because the KeyLoad field is already bound. */: not null }
            or { IncludeVectors: true }
            or { ScoreThreshold: not null })
        {
            throw new NotSupportedException(UnsupportedOptions);
        }

        var skip = options?.Skip ?? 0;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(top, int.MaxValue - skip);
        var embedding = searchValue switch
        {
            ReadOnlyMemory<float> memory => memory,
            float[] array => new ReadOnlyMemory<float>(array),
            _ => throw new ArgumentException(UnsupportedEmbedding, nameof(searchValue))
        };
        if (embedding.IsEmpty || embedding.Length != space.Dimension)
        {
            throw new ArgumentException(EmbeddingLengthMismatch, nameof(searchValue));
        }

        var request = new SearchRequest(partition, collection, VectorField: vectorField,
            Vector: ImmutableArray.Create(embedding.Span), Space: space, Limit: top + skip);
        return ExecuteAsync(request, skip, cancellationToken);
    }

    object? /* KLD0041: VectorData requires an object return for typed service lookup. */
        IVectorSearchable<DocumentResult>.GetService(Type serviceType,
            object? /* KLD0041: VectorData supplies an optional service key through this interface parameter. */ serviceKey)
    {
        ArgumentNullException.ThrowIfNull(serviceType) /* KLD0041: BCL null validation accepts the requested service Type through its object parameter. */;
        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(IVectorSearchable<DocumentResult>))
        {
            return this;
        }

        return serviceType == typeof(VectorStoreCollectionMetadata)
            ? new VectorStoreCollectionMetadata
            {
                VectorStoreSystemName = SystemName,
                VectorStoreName = partition.DatabaseId,
                CollectionName = collection
            }
            : null;
    }

    private async IAsyncEnumerable<VectorSearchResult<DocumentResult>> ExecuteAsync(SearchRequest request, int skip,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await client.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.IsFailed)
        {
            throw new VectorStoreException(string.Concat(SearchFailed, result.Problem.ErrorCode))
            {
                VectorStoreSystemName = SystemName,
                OperationName = nameof(SearchAsync)
            };
        }

        var documents = result.Value;
        for (var index = skip; index < documents.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hit = documents[index];
            yield return new(hit.Document, hit.Score);
        }
    }
}
