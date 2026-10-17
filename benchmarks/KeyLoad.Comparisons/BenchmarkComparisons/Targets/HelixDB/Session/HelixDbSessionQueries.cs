using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons.Targets;

public sealed partial class HelixDbTarget : IBenchmarkSessionQueries
{
    private const int SessionCandidateLimit = 2;
    private const int SessionExpectedRows = 1;
    private const int SessionFirstRow = 0;
    private const string SessionCardinalityMismatch = "SessionNativeQueryCardinalityMismatch";

    private bool ownsSessionNumberEqualityIndex;
    /// <inheritdoc/>
    public async Task BuildSessionIndexesAsync(CancellationToken cancellationToken)
    {
        await HelixDbIndexLifecycle.ExecuteAsync(http, HelixDbDocumentAst.Index(label, HelixDbNativeTokens.TokenNumber, range: false),
            Policy, timeProvider, cancellationToken).ConfigureAwait(false);
        ownsSessionNumberEqualityIndex = true;
    }

    /// <inheritdoc/>
    public async Task QuerySessionAsync(int ordinal, int stableStart, int stableCount, CancellationToken cancellationToken)
    {
        var expected = BenchmarkSessionQueryContract.Expected(ordinal, stableStart, stableCount);
        var filter = HelixDbProtocol.Node(HelixDbNativeTokens.TokenWhere, new JsonObject
        {
            [HelixDbNativeTokens.TokenInput] = HelixDbDocumentAst.Lookup(label, expected.Id),
            [HelixDbNativeTokens.TokenPredicate] = HelixDbProtocol.Compare(HelixDbNativeTokens.TokenEq,
                HelixDbNativeTokens.TokenNumber, HelixDbNativeTokens.TokenI64, expected.Number)
        });
        var query = HelixDbProtocol.Project(HelixDbProtocol.Limit(filter, SessionCandidateLimit), HelixDbNativeTokens.TokenId, HelixDbNativeTokens.TokenPayload);
        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(query, false), false, Policy,
            timeProvider, cancellationToken).ConfigureAwait(false);
        var rows = HelixDbProtocol.Rows(response);
        if (rows.GetArrayLength() != SessionExpectedRows)
        {
            throw new ComparisonFailureException(SessionCardinalityMismatch);
        }
        BenchmarkSessionQueryContract.Require(new(rows[SessionFirstRow].GetProperty(HelixDbNativeTokens.TokenId).GetString()!,
            rows[SessionFirstRow].GetProperty(HelixDbNativeTokens.TokenPayload).GetString()!), expected);
    }
}
