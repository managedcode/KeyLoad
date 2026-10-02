using System.Collections.Immutable;
using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal sealed record MongoReplicaProof(ClusterEvidence Evidence, string Version, IMongoClient[] SecondaryClients);

internal static class MongoReplicaVerifier
{
    public static async Task<MongoReplicaProof> VerifyAsync(string connectionString,
        IMongoDatabase adminDatabase, IMongoDatabase database,
        IMongoCollection<BsonDocument> documents, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var status = await adminDatabase.RunCommandAsync<BsonDocument>(
            new BsonDocument(MongoSchema.ReplicaSetStatusCommand, MongoSchema.CommandEnabledValue),
            ReadPreference.Primary, cancellationToken);
        var members = ReadMembers(status);
        var primaries = members.Where(IsPrimary).ToArray();
        var secondaries = members.Where(IsSecondary).ToArray();
        if (members.Length != MongoSchema.ReplicaNodeCount || primaries.Length != MongoSchema.PrimaryNodeCount
            || secondaries.Length != MongoSchema.SecondaryNodeCount)
        {
            throw new ComparisonFailureException(MongoSchema.FailureReplicaSetShape);
        }

        var clients = new List<IMongoClient>(secondaries.Length);
        try
        {
            clients.AddRange(secondaries.Select(member => CreateSecondaryClient(connectionString, member)));
            var versions = new List<string>(members.Length);
            foreach (var client in clients)
            {
                versions.Add(await ReadVersionAsync(client.GetDatabase(database.DatabaseNamespace.DatabaseName), ReadPreference.Secondary, cancellationToken));
            }
            versions.Insert(0, await ReadVersionAsync(database, ReadPreference.Primary, cancellationToken));
            if (versions.Distinct(StringComparer.Ordinal).Count() != MongoSchema.ConsistentVersionCount)
            {
                throw new ComparisonFailureException(MongoSchema.FailureReplicaMemberVersion);
            }

            var settings = MongoClientSettings.FromConnectionString(connectionString);
            settings.WriteConcern = MongoSchema.MajorityJournalWriteConcern;
            settings.ReadConcern = ReadConcern.Majority;
            settings.ReadPreference = ReadPreference.Primary;
            ValidateConcerns(settings);
            await VerifyCopiedProbeAsync(documents, clients.ToArray(), database.DatabaseNamespace.DatabaseName, timeoutSeconds, cancellationToken);
            var observations = BuildObservations(members, versions[0], settings);
            return new(new ClusterEvidence(members.Length, members.Length, MongoSchema.HealthyState, observations), versions[0], clients.ToArray());
        }
        catch (Exception)
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
            throw;
        }
    }

    private static BsonDocument[] ReadMembers(BsonDocument status)
        => status.GetValue(MongoSchema.MembersField).AsBsonArray.Select(value => value.AsBsonDocument).ToArray();

    private static bool IsPrimary(BsonDocument member)
        => IsHealthyDataBearing(member) && member.GetValue(MongoSchema.MemberStateField).AsString == MongoSchema.PrimaryState;

    private static bool IsSecondary(BsonDocument member)
        => IsHealthyDataBearing(member) && member.GetValue(MongoSchema.MemberStateField).AsString == MongoSchema.SecondaryState;

    private static bool IsHealthyDataBearing(BsonDocument member)
        => member.GetValue(MongoSchema.MemberHealthField).ToInt32() == MongoSchema.HealthyMemberValue
           && !member.GetValue(MongoSchema.MemberArbiterField, false).ToBoolean();

    private static MongoClient CreateSecondaryClient(string connectionString, BsonDocument member)
    {
        var host = member.GetValue(MongoSchema.MemberHostField).AsString;
        if (!Uri.TryCreate(MongoSchema.ConnectionPrefix + host, UriKind.Absolute, out var endpoint))
        {
            throw new ComparisonFailureException(MongoSchema.FailureMemberAddress);
        }
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        settings.Servers = [new MongoServerAddress(endpoint.Host, endpoint.Port)];
        settings.DirectConnection = true;
        settings.ReadPreference = ReadPreference.Secondary;
        settings.ReadConcern = ReadConcern.Majority;
        settings.WriteConcern = MongoSchema.MajorityJournalWriteConcern;
        return new MongoClient(settings);
    }

    private static async Task<string> ReadVersionAsync(IMongoDatabase database, ReadPreference readPreference,
        CancellationToken cancellationToken)
    {
        var build = await database.RunCommandAsync<BsonDocument>(
            new BsonDocument(MongoSchema.BuildInfoCommand, MongoSchema.CommandEnabledValue),
            readPreference, cancellationToken);
        return build.GetValue(MongoSchema.VersionField, BsonNull.Value).AsString;
    }

    private static void ValidateConcerns(MongoClientSettings settings)
    {
        if (settings.WriteConcern?.W?.ToString() != MongoSchema.MajorityMode || settings.WriteConcern.Journal != true
            || settings.ReadConcern?.Level != ReadConcernLevel.Majority
            || settings.ReadPreference?.ReadPreferenceMode != ReadPreferenceMode.Primary)
        {
            throw new ComparisonFailureException(MongoSchema.FailureReplicaSetShape);
        }
    }

    private static async Task VerifyCopiedProbeAsync(IMongoCollection<BsonDocument> primary,
        IMongoClient[] secondaries, string databaseName, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var probeId = MongoSchema.CollectionProbePrefix + Guid.NewGuid().ToString(MongoSchema.GuidFormat);
        var body = MongoSchema.ProbeJsonPrefix + probeId + MongoSchema.ProbeJsonSuffix;
        await primary.InsertOneAsync(new BsonDocument { [MongoSchema.IdField] = probeId, [MongoSchema.BodyField] = body }, cancellationToken: cancellationToken);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            foreach (var secondary in secondaries)
            {
                await WaitForSecondaryCopyAsync(secondary.GetDatabase(databaseName).GetCollection<BsonDocument>(MongoSchema.DocumentsCollection), probeId, body, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ComparisonFailureException(MongoSchema.FailureCopyProbeTimeout);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            await primary.DeleteOneAsync(new BsonDocument(MongoSchema.IdField, probeId), cancellationToken: cleanup.Token);
        }
    }

    private static async Task WaitForSecondaryCopyAsync(IMongoCollection<BsonDocument> collection,
        string probeId, string expectedBody, CancellationToken cancellationToken)
    {
        while (true)
        {
            var found = await collection.Find(new BsonDocument(MongoSchema.IdField, probeId)).FirstOrDefaultAsync(cancellationToken);
            if (found is not null)
            {
                if (found.GetValue(MongoSchema.BodyField).AsString != expectedBody)
                {
                    throw new ComparisonFailureException(MongoSchema.FailureDataCopyMissing);
                }
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(MongoSchema.ProbePollMilliseconds), cancellationToken);
        }
    }

    private static ImmutableArray<string> BuildObservations(BsonDocument[] members, string version, MongoClientSettings settings)
    {
        var roles = members.Select(member => member.GetValue(MongoSchema.MemberStateField).AsString)
            .Order(StringComparer.Ordinal).ToArray();
        var serverConcern = settings.WriteConcern!.W + MongoSchema.ConcernSeparator + settings.WriteConcern.Journal;
        return [MongoSchema.ObservationMembers + members.Length, MongoSchema.ObservationRoles + string.Join(',', roles),
            MongoSchema.ObservationVersions + version, MongoSchema.ObservationWriteConcern + serverConcern,
            MongoSchema.ObservationReadConcern + settings.ReadConcern!.Level,
            MongoSchema.ObservationReadPreference + settings.ReadPreference!.ReadPreferenceMode];
    }
}
