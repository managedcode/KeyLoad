using System.Collections.Immutable;
using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Reads only the selected engine's native connections and credentials before allocation.</summary>
internal sealed record IsolatedHostNativeSettings(string Image, string? Connection, ImmutableArray<Uri> Endpoints,
    ImmutableArray<string> Replicas, string? User, string? Password, string? ApiKey, string? AdminKey)
{
    internal static IsolatedHostNativeSettings Read(IConfiguration configuration, ComparisonWorkerSelection selection, string image)
    {
        var target = selection.Target;
        var connection = target is IsolatedHostConstants.Postgres or IsolatedHostConstants.Redis or IsolatedHostConstants.Rabbit
            or IsolatedHostConstants.Mongo or IsolatedHostConstants.Kurrent
            ? IsolatedHostSettings.Required(configuration, IsolatedHostConstants.Connection) : null;
        var endpoints = target is IsolatedHostConstants.KeyLoad or IsolatedHostConstants.Qdrant or IsolatedHostConstants.Rabbit
            or IsolatedHostConstants.Neo4j or IsolatedHostConstants.OpenSearch or IsolatedHostConstants.Kurrent
            or IsolatedHostConstants.SurrealDb or IsolatedHostConstants.HelixDb
            ? ReadEndpoints(configuration, selection.NodeCount) : ImmutableArray<Uri>.Empty;
        var replicas = target == IsolatedHostConstants.Redis
            ? ReadArray(configuration, IsolatedHostConstants.ReplicaConnections, selection.NodeCount - 1) : ImmutableArray<string>.Empty;
        var authenticated = target is IsolatedHostConstants.Rabbit or IsolatedHostConstants.Neo4j or IsolatedHostConstants.SurrealDb;
        var user = authenticated ? IsolatedHostSettings.Required(configuration, IsolatedHostConstants.User)
            : target == IsolatedHostConstants.OpenSearch ? configuration[IsolatedHostConstants.User] : null;
        var password = authenticated ? IsolatedHostSettings.Required(configuration, IsolatedHostConstants.Password)
            : target == IsolatedHostConstants.OpenSearch ? configuration[IsolatedHostConstants.Password] : null;
        ValidateCredentials(user, password);
        var apiKey = target == IsolatedHostConstants.Qdrant
            ? IsolatedHostSettings.Required(configuration, IsolatedHostConstants.ApiKey) : null;
        var admin = target == IsolatedHostConstants.KeyLoad
            ? IsolatedHostSettings.Required(configuration, ComparisonHostConstants.AdminKey) : null;
        return new(image, connection, endpoints, replicas, user, password, apiKey, admin);
    }

    private static ImmutableArray<Uri> ReadEndpoints(IConfiguration configuration, int count)
    {
        var values = ReadArray(configuration, IsolatedHostConstants.Endpoints, count);
        var result = ImmutableArray.CreateBuilder<Uri>(count);
        foreach (var value in values)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
                || endpoint.Scheme is not (IsolatedHostConstants.Http or IsolatedHostConstants.Https)
                || string.IsNullOrWhiteSpace(endpoint.Host) || endpoint.UserInfo.Length != 0
                || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.AbsolutePath != IsolatedHostConstants.RootPath)
            {
                throw InvalidSettings();
            }
            result.Add(endpoint);
        }
        var endpoints = result.MoveToImmutable();
        if (endpoints.Select(item => item.GetLeftPart(UriPartial.Authority)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != count)
        {
            throw InvalidSettings();
        }
        return endpoints;
    }

    private static ImmutableArray<string> ReadArray(IConfiguration configuration, string key, int count)
    {
        var section = configuration.GetSection(key);
        var children = section.GetChildren().ToArray();
        if (section.Value is not null || children.Length != count)
        {
            throw InvalidSettings();
        }
        var values = ImmutableArray.CreateBuilder<string>(count);
        for (var index = 0; index < count; index++)
        {
            var suffix = index.ToString(CultureInfo.InvariantCulture);
            if (!children.Any(item => item.Key == suffix))
            {
                throw InvalidSettings();
            }
            values.Add(IsolatedHostSettings.Required(configuration, key + IsolatedHostConstants.CredentialSeparator + suffix));
        }
        var result = values.MoveToImmutable();
        if (result.Distinct(StringComparer.Ordinal).Count() != count)
        {
            throw InvalidSettings();
        }
        return result;
    }

    private static void ValidateCredentials(string? user, string? password)
    {
        if (user is null && password is null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password)
            || user.Contains(IsolatedHostConstants.CredentialSeparator, StringComparison.Ordinal)
            || user.Contains('\r', StringComparison.Ordinal) || user.Contains('\n', StringComparison.Ordinal)
            || password.Contains('\r', StringComparison.Ordinal) || password.Contains('\n', StringComparison.Ordinal))
        {
            throw InvalidSettings();
        }
    }

    private static InvalidOperationException InvalidSettings() => new(IsolatedHostConstants.Failure);
}
