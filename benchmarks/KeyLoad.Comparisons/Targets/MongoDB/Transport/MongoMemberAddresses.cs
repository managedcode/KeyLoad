using System.Collections.Immutable;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Maps each replica-set member name the server advertises to the endpoint its container publishes to the client host.</summary>
/// <remarks>The members keep their container-network names so they replicate to each other; the workload client runs on the
/// host, where only the published endpoints resolve. The seed list and the published endpoints share the node order, and the
/// first member is the bootstrap's preferred primary, so the client connects directly to its published endpoint.</remarks>
internal sealed class MongoMemberAddresses
{
    private const string EndpointMismatch = "MongoMemberEndpointMismatch";
    private const int PrimaryMemberIndex = 0;
    private const int MinimumPort = 1;
    private readonly ImmutableDictionary<(string Host, int Port), MongoServerAddress> published;
    private readonly MongoServerAddress primary;

    private MongoMemberAddresses(ImmutableDictionary<(string Host, int Port), MongoServerAddress> published,
        MongoServerAddress primary)
    {
        this.published = published;
        this.primary = primary;
    }

    internal static MongoMemberAddresses Create(string connectionString, ImmutableArray<Uri> endpoints)
    {
        var members = new MongoUrl(connectionString).Servers.ToArray();
        if (endpoints.IsDefault || members.Length == PrimaryMemberIndex || endpoints.Length != members.Length)
        {
            throw new ComparisonFailureException(EndpointMismatch);
        }
        var builder = ImmutableDictionary.CreateBuilder<(string Host, int Port), MongoServerAddress>();
        for (var index = PrimaryMemberIndex; index < members.Length; index++)
        {
            var endpoint = endpoints[index];
            if (!endpoint.IsAbsoluteUri || endpoint.Port < MinimumPort)
            {
                throw new ComparisonFailureException(EndpointMismatch);
            }
            builder.Add((members[index].Host, members[index].Port), new MongoServerAddress(endpoint.Host, endpoint.Port));
        }
        var map = builder.ToImmutable();
        return new(map, map[(members[PrimaryMemberIndex].Host, members[PrimaryMemberIndex].Port)]);
    }

    /// <summary>Connects the settings directly to the published endpoint of the preferred primary.</summary>
    internal MongoClientSettings Apply(MongoClientSettings settings)
        => Direct(settings, primary);

    /// <summary>Connects the settings directly to the published endpoint of one advertised member.</summary>
    internal MongoClientSettings ApplyMember(MongoClientSettings settings, string host, int port)
        => Direct(settings, published.TryGetValue((host, port), out var endpoint)
            ? endpoint : throw new ComparisonFailureException(EndpointMismatch));

    private static MongoClientSettings Direct(MongoClientSettings settings, MongoServerAddress endpoint)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Servers = [endpoint];
        settings.DirectConnection = true;
        return settings;
    }
}
