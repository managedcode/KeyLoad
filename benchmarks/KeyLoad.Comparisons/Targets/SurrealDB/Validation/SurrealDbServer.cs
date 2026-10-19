namespace KeyLoad.Comparisons.Targets;

internal static class SurrealDbServer
{
    private const string ExpectedVersion = "surrealdb-3.2.4+20260803.93ab219";
    internal static async Task<string> VerifyAsync(HttpClient http, ComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        using var operationDeadline = new ComparisonCancellationSource(timeProvider, token);
        operationDeadline.CancelAfter(policy.OperationTimeout);
        token = operationDeadline.Token;
        using var response = await http.GetAsync(new Uri(SurrealDbTokens.TokenVersion, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var version = (await ComparisonResponse.ReadTextAsync(response.Content, policy, token).ConfigureAwait(false)).Trim();
        if (version != ExpectedVersion)
        {
            throw new ComparisonFailureException(SurrealDbTokens.TokenSurrealDbServerVersionMismatch);
        }

        return version;
    }
}
