using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.ComparisonHost.Deployment;

/// <summary>
/// SurrealQL over the SurrealDB HTTP <c>/sql</c> endpoint in the <c>main</c> namespace and database, shared by the
/// SurrealDB drivers. The published root credential is exchanged at <c>/signin</c> for a bearer token, renewed well within
/// its one-hour lifetime, because SurrealDB verifies a Basic credential with Argon2 on every request.
/// </summary>
internal sealed class SurrealSql(BenchmarkConnection connection) : IDisposable
{
    private const string SqlRoute = "sql";
    private const string SignInRoute = "signin";
    private const string Namespace = "main";
    private const string Database = "main";
    private const string NamespaceHeader = "Surreal-NS";
    private const string DatabaseHeader = "Surreal-DB";
    private const string JsonMediaType = "application/json";
    private const string TextMediaType = "text/plain";
    private const string BearerScheme = "Bearer";
    private const string UserField = "user";
    private const string PasswordField = "pass";
    private const string TokenField = "token";
    private const string StatusField = "status";
    private const string ResultField = "result";
    private const string Succeeded = "OK";
    private const char RecordSeparator = ':';
    private const char KeyStart = '\u27E8';
    private const char KeyEnd = '\u27E9';
    private const char QuotedKey = '`';
    private const char KeyDash = '-';
    private const char KeyUnderscore = '_';
    private const string Failed = "SurrealDB ";
    private const string Separator = ": ";
    private const string InvalidResponse = "returned a response that is not a statement list: ";
    private const string NoToken = "SurrealDB sign-in returned no token.";
    private const string InvalidRecord = "SurrealDB returned an unexpected record id: ";
    private const string InvalidKey = "The record key is not a plain identifier: ";
    private static readonly Uri SqlPath = new(SqlRoute, UriKind.Relative);
    private static readonly Uri SignInPath = new(SignInRoute, UriKind.Relative);
    private static readonly TimeSpan Renewal = TimeSpan.FromMinutes(30);
    private readonly TimeProvider clock = TimeProvider.System;
    private readonly HttpClient http = connection.CreateHttpClient(0);
    private readonly string user = connection.User;
    private readonly string password = connection.Password;
    private Credential? credential;

    /// <summary>Runs the statements and returns their results; throws with SurrealDB's message when any statement failed.</summary>
    internal async Task<JsonDocument> QueryAsync(string statements, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SqlPath)
        {
            Content = new StringContent(statements, Encoding.UTF8, TextMediaType)
        };
        request.Headers.Authorization = await AuthorizeAsync(token);
        request.Headers.TryAddWithoutValidation(NamespaceHeader, Namespace);
        request.Headers.TryAddWithoutValidation(DatabaseHeader, Database);
        request.Headers.Accept.ParseAdd(JsonMediaType);
        using var response = await http.SendAsync(request, token);
        var document = JsonDocument.Parse(await ReadAsync(response, token));
        if (!Accepted(document.RootElement, out var error))
        {
            document.Dispose();
            throw new InvalidOperationException(Failed + error);
        }
        return document;
    }

    /// <summary>Runs statements whose results are not needed.</summary>
    internal async Task ExecuteAsync(string statements, CancellationToken token)
        => (await QueryAsync(statements, token)).Dispose();

    /// <summary>The result of the statement at <paramref name="statement"/> in a response.</summary>
    internal static JsonElement Result(JsonDocument response, int statement)
        => response.RootElement[statement].GetProperty(ResultField);

    /// <summary>The record id literal <c>table:⟨key⟩</c>; keys are plain identifiers, so they need no escaping.</summary>
    internal static string Record(string table, string key)
    {
        if (key.Length == 0 || !key.All(character => char.IsAsciiLetterOrDigit(character) || character is KeyDash or KeyUnderscore))
        {
            throw new ArgumentException(InvalidKey + key, nameof(key));
        }
        return table + RecordSeparator + KeyStart + key + KeyEnd;
    }

    /// <summary>The key of a record id SurrealDB returned as <c>table:key</c>, <c>table:⟨key⟩</c> or <c>table:`key`</c>.</summary>
    internal static string Key(string table, JsonElement id)
    {
        var value = id.GetString() ?? string.Empty;
        if (value.Length <= table.Length + 1 || !value.StartsWith(table, StringComparison.Ordinal) || value[table.Length] != RecordSeparator)
        {
            throw new InvalidDataException(InvalidRecord + value);
        }
        var key = value[(table.Length + 1)..];
        return key.Length > 1 && (key[0], key[^1]) is (KeyStart, KeyEnd) or (QuotedKey, QuotedKey) ? key[1..^1] : key;
    }

    public void Dispose() => http.Dispose();

    /// <summary>The current bearer token; a caller that finds it missing, failed or due for renewal signs in again.</summary>
    private async Task<AuthenticationHeaderValue> AuthorizeAsync(CancellationToken token)
    {
        var current = Volatile.Read(ref credential);
        if (current is null || current.Header.IsFaulted || current.Header.IsCanceled || clock.GetElapsedTime(current.Issued) >= Renewal)
        {
            var renewed = new Credential(SignInAsync(token), clock.GetTimestamp());
            _ = Interlocked.CompareExchange(ref credential, renewed, current);
            current = renewed;
        }
        return await current.Header;
    }

    private async Task<AuthenticationHeaderValue> SignInAsync(CancellationToken token)
    {
        var body = new JsonObject
        {
            [UserField] = user,
            [PasswordField] = password
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, SignInPath)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, JsonMediaType)
        };
        request.Headers.Accept.ParseAdd(JsonMediaType);
        using var response = await http.SendAsync(request, token);
        using var document = JsonDocument.Parse(await ReadAsync(response, token));
        return document.RootElement.TryGetProperty(TokenField, out var issued) && issued.GetString() is { Length: > 0 } value
            ? new(BearerScheme, value)
            : throw new InvalidOperationException(NoToken);
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken token)
    {
        var text = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(Failed + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + Separator + text,
                null, response.StatusCode);
        }
        return text;
    }

    private static bool Accepted(JsonElement root, out string error)
    {
        error = string.Empty;
        if (root.ValueKind != JsonValueKind.Array)
        {
            error = InvalidResponse + root.GetRawText();
            return false;
        }
        foreach (var statement in root.EnumerateArray())
        {
            if (!statement.TryGetProperty(StatusField, out var status) || status.GetString() != Succeeded)
            {
                error = statement.TryGetProperty(ResultField, out var result) ? result.ToString() : statement.GetRawText();
                return false;
            }
        }
        return true;
    }

    private sealed record Credential(Task<AuthenticationHeaderValue> Header, long Issued);
}
