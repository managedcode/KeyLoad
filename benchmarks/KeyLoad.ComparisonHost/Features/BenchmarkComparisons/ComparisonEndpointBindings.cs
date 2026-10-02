using System.Collections.Immutable;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Validates endpoints and credentials required by the comparison host callers.</summary>
internal static class ComparisonEndpointBindings
{
    private const string HttpScheme = "http";
    private const string HttpsScheme = "https";
    private const string UriRootPath = "/";
    private const int PrimaryEndpointIndex = 0;
    private const string FirstEndpointIndex = "0";
    private const string SecondEndpointIndex = "1";
    private const string ThirdEndpointIndex = "2";

    internal static ImmutableArray<Uri> ReadKeyLoadEndpoints(IConfiguration configuration, Uri primary)
    {
        var endpoints = ImmutableArray.CreateBuilder<Uri>(ComparisonHostConstants.KeyLoadEndpointCount);
        for (var index = 0; index < ComparisonHostConstants.KeyLoadEndpointCount; index++)
        {
            var key = ComparisonHostConstants.KeyLoadEndpoints + ":" + index.ToString(CultureInfo.InvariantCulture);
            if (!TryParseHttpOrigin(ComparisonHostSettings.RequiredValue(configuration, key), out var endpoint))
            {
                throw InvalidKeyLoadEndpoints();
            }

            endpoints.Add(endpoint);
        }

        var children = configuration.GetSection(ComparisonHostConstants.KeyLoadEndpoints).GetChildren().ToArray();
        if (children.Length != ComparisonHostConstants.KeyLoadEndpointCount ||
            children.Any(child => child.Key is not (FirstEndpointIndex or SecondEndpointIndex or ThirdEndpointIndex)))
        {
            throw InvalidKeyLoadEndpoints();
        }

        var result = endpoints.MoveToImmutable();
        var seenOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in result)
        {
            if (!seenOrigins.Add(endpoint.GetLeftPart(UriPartial.Authority)))
            {
                throw InvalidKeyLoadEndpoints();
            }
        }

        if (!IsHttpOrigin(primary) || !SameOrigin(primary, result[PrimaryEndpointIndex]))
        {
            throw InvalidKeyLoadEndpoints();
        }

        return result;
    }

    internal static Uri ReadRabbitManagementEndpoint(IConfiguration configuration)
    {
        var value = ComparisonHostSettings.RequiredValue(configuration,
            ComparisonHostConstants.RabbitManagementEndpoint);
        if (!TryParseHttpOrigin(value, out var endpoint))
        {
            throw new InvalidOperationException(ComparisonHostConstants.RabbitManagementEndpointInvalidCode);
        }

        return endpoint;
    }

    internal static void ValidateRabbitCredentials(string user, string password)
    {
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password) ||
            user.Contains(ComparisonHostConstants.UserPasswordSeparator, StringComparison.Ordinal) ||
            ContainsLineBreak(user) || ContainsLineBreak(password))
        {
            throw new InvalidOperationException(ComparisonHostConstants.RabbitManagementCredentialsInvalidCode);
        }
    }

    internal static AuthenticationHeaderValue CreateRabbitAuthorization(string user, string password)
    {
        var credential = user + ComparisonHostConstants.UserPasswordSeparator + password;
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(credential));
        return new AuthenticationHeaderValue(ComparisonHostConstants.BasicAuthenticationScheme, token);
    }

    private static bool TryParseHttpOrigin(string value, out Uri endpoint)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && IsHttpOrigin(parsed))
        {
            endpoint = parsed;
            return true;
        }

        endpoint = null!;
        return false;
    }

    private static bool IsHttpOrigin(Uri endpoint)
        => (string.Equals(endpoint.Scheme, HttpScheme, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(endpoint.Scheme, HttpsScheme, StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(endpoint.Host) && endpoint.UserInfo.Length == 0 &&
            endpoint.Query.Length == 0 && endpoint.Fragment.Length == 0 &&
            string.Equals(endpoint.AbsolutePath, UriRootPath, StringComparison.Ordinal);

    private static bool SameOrigin(Uri left, Uri right)
        => string.Equals(left.GetLeftPart(UriPartial.Authority), right.GetLeftPart(UriPartial.Authority),
            StringComparison.OrdinalIgnoreCase);

    private static bool ContainsLineBreak(string value)
        => value.Contains('\r', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal);

    private static InvalidOperationException InvalidKeyLoadEndpoints()
        => new(ComparisonHostConstants.KeyLoadEndpointInvalidCode);
}
