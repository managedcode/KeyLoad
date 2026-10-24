using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Exchanges the client's root Basic credential for a fresh bearer token whenever new clients are about to copy it.</summary>
/// <remarks>SurrealDB verifies a Basic credential with Argon2 on every HTTP request; a root token is checked by its signature and
/// expiry. Root tokens last one hour, so each set of clients receives a token issued when it opens.</remarks>
internal sealed class SurrealDbBearerToken
{
    private const string SigninPath = "signin";
    private const string BasicScheme = "Basic";
    private const string BearerScheme = "Bearer";
    private const char CredentialSeparator = ':';
    private const string UserField = "user";
    private const string PasswordField = "pass";
    private const string TokenField = "token";
    private const string SigninRejected = "SurrealDbSigninRejected";
    private const int Missing = -1;
    private AuthenticationHeaderValue? basic;

    /// <summary>Signs in with the original root credential and sets the new token as the client's default credential.</summary>
    internal async Task RenewAsync(HttpClient http, ComparisonExecutionOptions policy, CancellationToken cancellationToken)
    {
        basic ??= http.DefaultRequestHeaders.Authorization is { Scheme: BasicScheme, Parameter: not null } original
            ? original : throw new ComparisonFailureException(SigninRejected);
        var credential = Encoding.UTF8.GetString(Convert.FromBase64String(basic.Parameter!));
        var separator = credential.IndexOf(CredentialSeparator, StringComparison.Ordinal);
        if (separator == Missing)
        {
            throw new ComparisonFailureException(SigninRejected);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, SigninPath)
        {
            Content = JsonContent.Create(new Dictionary<string, string>
            {
                [UserField] = credential[..separator],
                [PasswordField] = credential[(separator + 1)..]
            })
        };
        request.Headers.Authorization = basic;
        request.Headers.Accept.ParseAdd(SurrealDbTokens.TokenApplicationJson);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ComparisonFailureException(SigninRejected);
        }
        using var document = await ComparisonResponse.ReadJsonAsync(response.Content, policy, cancellationToken).ConfigureAwait(false);
        var token = document.RootElement.TryGetProperty(TokenField, out var value) ? value.GetString() : null;
        http.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(token)
            ? throw new ComparisonFailureException(SigninRejected) : new(BearerScheme, token);
    }
}
