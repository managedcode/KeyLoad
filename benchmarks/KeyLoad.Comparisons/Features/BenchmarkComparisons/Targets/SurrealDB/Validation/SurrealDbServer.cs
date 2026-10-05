namespace KeyLoad.Comparisons.Targets;

internal static class SurrealDbServer
{
    private const string ExpectedVersion = "surrealdb-3.2.4";
    internal static async Task<string> VerifyAsync(HttpClient http, NativeComparisonExecutionOptions policy, CancellationToken token)
    {
        using var operationDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        operationDeadline.CancelAfter(policy.OperationTimeout);
        token = operationDeadline.Token;
        using var response = await http.GetAsync(new Uri(SurrealDbNativeTokens.TokenVersion, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var version = (await NativeComparisonResponse.ReadTextAsync(response.Content, policy, token).ConfigureAwait(false)).Trim();
        if (version != ExpectedVersion)
        {
            throw new ComparisonFailureException(SurrealDbNativeTokens.TokenSurrealDbServerVersionMismatch);
        }

        return version;
    }
}
