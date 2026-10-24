using System.Collections.Immutable;
using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

/// <summary>Reads only the selected engine's execution connections and credentials before allocation.</summary>
[KeyLoad.ConfigurationBinding]
internal sealed record RuntimeIsolatedHostSettings(string? Image, string? Connection, ImmutableArray<Uri> Endpoints,
    ImmutableArray<string> Replicas, string? User, string? Password, string? ApiKey, string? AdminKey)
{
    private const char CarriageReturnCharacter = '\r';
    private const char LineFeedCharacter = '\n';
    internal static RuntimeIsolatedHostSettings Read(IConfiguration configuration, BenchmarkSessionSelection selection, string? image)
    {
        const int PrimaryReplicaCount = 1;

        var target = selection.Target;
        var connection = target is IsolatedHostConstants.Postgres or IsolatedHostConstants.Redis or IsolatedHostConstants.Rabbit
            or IsolatedHostConstants.Mongo or IsolatedHostConstants.Kurrent
            or BenchmarkAdditionalTargets.Timescale or BenchmarkAdditionalTargets.SqlServer or BenchmarkAdditionalTargets.MySql
            ? IsolatedHostSettings.Required(configuration, IsolatedHostConstants.Connection) : null;
        var endpoints = target is IsolatedHostConstants.KeyLoad or IsolatedHostConstants.Qdrant or IsolatedHostConstants.Rabbit
            or IsolatedHostConstants.Neo4j or IsolatedHostConstants.OpenSearch or IsolatedHostConstants.Kurrent
            or IsolatedHostConstants.SurrealDb or IsolatedHostConstants.HelixDb
            or BenchmarkAdditionalTargets.ClickHouse or BenchmarkAdditionalTargets.InfluxDb
            ? ReadEndpoints(configuration, selection.NodeCount, tcp: false)
            : target == IsolatedHostConstants.Mongo ? ReadEndpoints(configuration, selection.NodeCount, tcp: true) : ImmutableArray<Uri>.Empty;
        var replicas = target == IsolatedHostConstants.Redis
            ? ReadArray(configuration, IsolatedHostConstants.ReplicaConnections, selection.NodeCount - PrimaryReplicaCount) : ImmutableArray<string>.Empty;
        var authenticated = target is IsolatedHostConstants.Rabbit or IsolatedHostConstants.Neo4j or IsolatedHostConstants.SurrealDb
            or BenchmarkAdditionalTargets.ClickHouse;
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

    // MongoDB members publish raw TCP endpoints; every other endpoint-bound target is an HTTP(S) origin.
    private static ImmutableArray<Uri> ReadEndpoints(IConfiguration configuration, int count, bool tcp)
    {
        const int EmptyUserInfoLength = 0;
        const int EmptyQueryLength = 0;
        const int EmptyFragmentLength = 0;

        var values = ReadArray(configuration, IsolatedHostConstants.Endpoints, count);
        var result = ImmutableArray.CreateBuilder<Uri>(count);
        foreach (var value in values)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
                || (tcp ? endpoint.Scheme != IsolatedHostConstants.Tcp
                    : endpoint.Scheme is not (IsolatedHostConstants.Http or IsolatedHostConstants.Https))
                || string.IsNullOrWhiteSpace(endpoint.Host) || endpoint.UserInfo.Length != EmptyUserInfoLength
                || endpoint.Query.Length != EmptyQueryLength || endpoint.Fragment.Length != EmptyFragmentLength || endpoint.AbsolutePath != IsolatedHostConstants.RootPath)
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
        const int FirstEntryIndex = 0;

        var section = configuration.GetSection(key);
        var children = section.GetChildren().ToArray();
        if (section.Value is not null || children.Length != count)
        {
            throw InvalidSettings();
        }
        var values = ImmutableArray.CreateBuilder<string>(count);
        for (var index = FirstEntryIndex; index < count; index++)
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
            || user.Contains(CarriageReturnCharacter, StringComparison.Ordinal) || user.Contains(LineFeedCharacter, StringComparison.Ordinal)
            || password.Contains(CarriageReturnCharacter, StringComparison.Ordinal) || password.Contains(LineFeedCharacter, StringComparison.Ordinal))
        {
            throw InvalidSettings();
        }
    }

    private static InvalidOperationException InvalidSettings() => new(IsolatedHostConstants.Failure);
}
