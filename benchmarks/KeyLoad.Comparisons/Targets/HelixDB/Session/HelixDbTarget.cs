using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;

public sealed partial class HelixDbTarget : IBenchmarkSessionQueries
{
    private const int SessionCandidateLimit = 2;
    private const string SessionCardinalityMismatch = "SessionQueryCardinalityMismatch";

    private bool ownsSessionNumberEqualityIndex;
    /// <inheritdoc/>
    public async Task BuildSessionIndexesAsync(CancellationToken cancellationToken)
    {
        await HelixDbIndexLifecycle.ExecuteAsync(http, HelixDbDocumentAst.Index(label, HelixDbTokens.TokenNumber, range: false),
            Policy, timeProvider, cancellationToken).ConfigureAwait(false);
        ownsSessionNumberEqualityIndex = true;
    }

    /// <inheritdoc/>
    public async Task QuerySessionAsync(int ordinal, int stableStart, int stableCount, CancellationToken cancellationToken)
    {
        var expected = BenchmarkSessionQueryContract.Expected(ordinal, stableStart, stableCount);
        var filter = HelixDbProtocol.Node(HelixDbTokens.TokenWhere, new JsonObject
        {
            [HelixDbTokens.TokenInput] = HelixDbDocumentAst.Lookup(label, expected.Id),
            [HelixDbTokens.TokenPredicate] = HelixDbProtocol.Compare(HelixDbTokens.TokenEq,
                HelixDbTokens.TokenNumber, HelixDbTokens.TokenI64, expected.Number)
        });
        var query = HelixDbProtocol.Project(HelixDbProtocol.Limit(filter, SessionCandidateLimit), HelixDbTokens.TokenId, HelixDbTokens.TokenPayload);
        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(query, false), false, Policy,
            timeProvider, cancellationToken).ConfigureAwait(false);
        var rows = HelixDbProtocol.Rows(response);
        if (rows.GetArrayLength() != 1)
        {
            throw new ComparisonFailureException(SessionCardinalityMismatch);
        }
        BenchmarkSessionQueryContract.Require(new(rows[0].GetProperty(HelixDbTokens.TokenId).GetString()!,
            rows[0].GetProperty(HelixDbTokens.TokenPayload).GetString()!), expected);
    }
}
