using System.Runtime.InteropServices;
using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

internal sealed record RedisNodeIdentity(string RunId, string Version)
{
    public const string InvalidError = "RedisNativeIdentityInvalid";
    public const string DuplicateError = "RedisNativeIdentityNotUnique";
    public const string VersionError = "RedisNativeVersionMismatch";
    public const string ChangedError = "RedisNativeIdentityChanged";
    public const string ObservationPrefix = "native node identity: ";
    public const string VersionSeparator = " version=";
    private const string RunIdSeparator = " run_id=";
    private const string PrimaryRoleObservation = "primary";
    private const string ReplicaRolePrefix = "replica-";
    private const string SingleStateObservation = "observed ROLE=master and connected_slaves=0";
    private const string TwoReplicaObservation = "primary ROLE=master with two connected replicas";
    private const string OneReplicaObservation = "primary ROLE=master with one connected replica";
    private const string ReplicaProtocolObservation = "direct endpoints report ROLE=slave and matching INFO master_host/master_port";
    private const string ReplicaPayloadObservation = "payload read from each endpoint with DemandReplica";
    private const string SingleAofObservation = "AOF appendonly=yes and appendfsync=always";
    private const string ReplicatedAofObservation = "AOF appendonly=yes and appendfsync=always on every observed node";

    public static async Task<RedisNodeIdentity> ReadAsync(IServer server, CommandFlags flags, CancellationToken token)
    {
        var info = await RedisNativeProtocol.ReadInfoAsync(server, RedisNativeProtocol.ServerSection, flags, token);
        var runId = info.GetValueOrDefault(RedisNativeProtocol.RunIdField);
        var version = info.GetValueOrDefault(RedisNativeProtocol.VersionField);
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(version))
        {
            throw new ComparisonFailureException(InvalidError);
        }

        return new(runId, version);
    }

    public static void RequireUniqueVersionedSet(IReadOnlyCollection<RedisNodeIdentity> identities,
        RedisNodeIdentity primary, int expectedCount)
    {
        if (identities.Count != expectedCount || identities.Any(identity => identity.Version != primary.Version))
        {
            throw new ComparisonFailureException(VersionError);
        }

        if (identities.Select(identity => identity.RunId).Distinct(StringComparer.Ordinal).Count() != expectedCount)
        {
            throw new ComparisonFailureException(DuplicateError);
        }
    }

    public static void RequireUnchanged(RedisNodeIdentity current, RedisNodeIdentity original)
    {
        if (current != original)
        {
            throw new ComparisonFailureException(ChangedError);
        }
    }

    public string Observation(string role) => ObservationPrefix + role + RunIdSeparator + RunId + VersionSeparator + Version;

    public static ClusterEvidence SingleEvidence(RedisNodeIdentity identity, string state)
        => new(1, 1, state, [SingleStateObservation, identity.Observation(PrimaryRoleObservation), SingleAofObservation]);

    public static ClusterEvidence ReplicatedEvidence(RedisNodeIdentity primary, RedisNodeIdentity[] replicas, string state)
    {
        var observations = new List<string>
        {
            replicas.Length == 2 ? TwoReplicaObservation : OneReplicaObservation,
            primary.Observation(PrimaryRoleObservation),
            ReplicaProtocolObservation,
            ReplicaPayloadObservation,
            ReplicatedAofObservation
        };
        for (var index = 0; index < replicas.Length; index++)
        {
            observations.Add(replicas[index].Observation(ReplicaRolePrefix + (index + 1)));
        }

        return new(replicas.Length + 1, replicas.Length + 1, state,
            ImmutableCollectionsMarshal.AsImmutableArray(observations.ToArray()));
    }
}
