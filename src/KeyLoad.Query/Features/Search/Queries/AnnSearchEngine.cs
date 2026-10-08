using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query;

public sealed partial class SearchEngine
{
    private readonly IAnnProjection? annProjection;

    internal SearchEngine(DatabaseEngine database, IOptions<QueryExecutionOptions> options,
        ITextProjection? textProjection, IAnnProjection annProjection)
        : this(database, options, textProjection)
    {
        ArgumentNullException.ThrowIfNull(annProjection);
        this.annProjection = annProjection;
    }

    /// <summary>Completes one explicitly versioned vector-only native generation read under current policy.</summary>
    /// <param name="principalId">The freshly authenticated persisted caller identity.</param>
    /// <param name="request">The provisioned generation and bounded vector search scope.</param>
    /// <param name="cancellationToken">Original cancellation for admission, native work and full projection.</param>
    /// <returns>A complete projected page with actual same-cut search mode and completeness.</returns>
    public Task<AnnSearchPage> ApproximateSearchAsync(string principalId,
        ApproximateSearchRequest request, CancellationToken cancellationToken = default)
        => AnnPublicSearchExecutor.ExecuteAsync(database, annProjection, principalId, request,
            configuration, cancellationToken);
}
