using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Owns SurrealDB HTTP framing and its native response-envelope validation.</summary>
internal static class SurrealDbSqlTransport
{
    private const int EmptyResultCount = 0;
    private const string SuccessfulStatus = "OK";
    private const string ApiPath = "sql";
    private const string Namespace = "main";
    private const string Database = "main";
    private const string InvalidResponse = "SurrealDbInvalidResponse";
    private const string RequestFailed = "SurrealDbRequestFailed";
    private const string StatusKey = "status";
    private const string ResultKey = "result";
    internal static async Task<JsonDocument> QueryAsync(HttpClient http, string sql, NativeComparisonExecutionOptions policy, CancellationToken cancellationToken)
    {
        using var operationDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationDeadline.CancelAfter(policy.OperationTimeout);
        cancellationToken = operationDeadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiPath)
        {
            Content = new StringContent(sql, Encoding.UTF8, SurrealDbNativeTokens.TokenTextPlain)
        };
        request.Headers.TryAddWithoutValidation(SurrealDbNativeTokens.TokenSurrealNS, Namespace);
        request.Headers.TryAddWithoutValidation(SurrealDbNativeTokens.TokenDatabaseHeader, Database);
        request.Headers.Accept.ParseAdd(SurrealDbNativeTokens.TokenApplicationJson);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ComparisonFailureException(RequestFailed);
        }

        JsonDocument? document = null;
        try
        {
            document = await NativeComparisonResponse.ReadJsonAsync(response.Content, policy, cancellationToken).ConfigureAwait(false);
            ValidateResponse(document.RootElement);
            return document;
        }
        catch (JsonException error)
        {
            document?.Dispose();
            throw new ComparisonFailureException(InvalidResponse, error);
        }
        catch (Exception)
        {
            document?.Dispose();
            throw;
        }
    }

    internal static async Task ExecuteAsync(HttpClient http, string sql, NativeComparisonExecutionOptions policy, CancellationToken cancellationToken)
    {
        using var response = await QueryAsync(http, sql, policy, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateResponse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == EmptyResultCount)
        {
            throw new InvalidDataException(InvalidResponse);
        }

        foreach (var statement in root.EnumerateArray())
        {
            var status = SurrealDbVectorProtocol.ReadRequiredString(statement, StatusKey);
            if (!status.Equals(SuccessfulStatus, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(SurrealDbNativeTokens.TokenSurrealDbStatementFailed);
            }

            _ = statement.GetProperty(ResultKey);
        }
    }
}
