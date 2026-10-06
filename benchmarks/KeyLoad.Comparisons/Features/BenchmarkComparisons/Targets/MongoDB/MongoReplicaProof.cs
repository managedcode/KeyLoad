using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal sealed record MongoReplicaProof(ClusterEvidence Evidence, string Version, IMongoClient[] SecondaryClients);

internal static class MongoReplicaVerifier
{
    public static async Task<MongoReplicaProof> VerifyAsync(string connectionString, IMongoDatabase adminDatabase, IMongoDatabase database,
        IMongoCollection<BsonDocument> documents, ComparisonTopology topology, IComparisonCorpus dataset,
        IOptions<ComparisonLifecycleOptions> lifecycleOptions, CancellationToken cancellationToken)
    {
        const int SingleItemCount = 1;
        const int FirstElementIndex = 0;
        const int NoObservedItems = 0;

        var status = await adminDatabase.RunCommandAsync<BsonDocument>(
            new BsonDocument(MongoSchema.ReplicaSetStatusCommand, MongoSchema.CommandEnabledValue),
            ReadPreference.Primary, cancellationToken);
        var settings = MongoTarget.CreateSettings(connectionString, SingleItemCount);
        var members = ValidateMembers(status, topology, settings.ReplicaSetName);
        var secondaries = members.Where(MongoReplicaMembers.IsSecondary).ToArray();

        var clients = new List<IMongoClient>(secondaries.Length);
        try
        {
            clients.AddRange(secondaries.Select(member => CreateSecondaryClient(connectionString, member)));
            var versions = new List<string>(members.Length);
            for (var index = FirstElementIndex; index < clients.Count; index++)
            {
                var client = clients[index];
                await VerifySameSetAsync(client, topology, status, secondaries[index], cancellationToken);
                versions.Add(await ReadVersionAsync(client.GetDatabase(database.DatabaseNamespace.DatabaseName), ReadPreference.Secondary, cancellationToken));
            }
            versions.Insert(NoObservedItems, await ReadVersionAsync(database, ReadPreference.Primary, cancellationToken));
            if (versions.Distinct(StringComparer.Ordinal).Count() != MongoSchema.ConsistentVersionCount)
            {
                throw new ComparisonFailureException(MongoSchema.FailureReplicaMemberVersion);
            }

            ValidateConcerns(settings);
            await VerifyCopiedProbeAsync(primary: documents, secondaries: clients.ToArray(),
                databaseName: database.DatabaseNamespace.DatabaseName, timeoutSeconds: dataset.Settings.TimeoutSeconds,
                cancellationToken: cancellationToken, pollInterval: lifecycleOptions.Value.MongoReadinessPollInterval);
            await MongoSeededCopies.VerifyAsync(clients: clients, databaseName: database.DatabaseNamespace.DatabaseName, dataset: dataset,
                cancellationToken: cancellationToken, lifecycleOptions: lifecycleOptions);
            var observations = BuildObservations(status, members, versions[FirstElementIndex], settings);
            return new(new ClusterEvidence(members.Length, members.Length, MongoSchema.HealthyState, observations), versions[FirstElementIndex], clients.ToArray());
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

    internal static BsonDocument[] ValidateMembers(BsonDocument status, ComparisonTopology topology, string? expectedSet)
        => MongoReplicaMembers.Validate(status, topology, expectedSet);

    private static async Task VerifySameSetAsync(IMongoClient client, ComparisonTopology topology, BsonDocument expected, BsonDocument expectedMember,
        CancellationToken cancellationToken)
    {
        var status = await client.GetDatabase(MongoSchema.AdminDatabase).RunCommandAsync<BsonDocument>(
            new BsonDocument(MongoSchema.ReplicaSetStatusCommand, MongoSchema.CommandEnabledValue), ReadPreference.Secondary, cancellationToken);
        var members = ValidateMembers(status, topology, expected[MongoSchema.ReplicaStatusSetField].AsString);
        MongoReplicaMembers.ValidateDirectMember(status, expectedMember);
        var original = MongoReplicaMembers.Read(expected);
        if (members.Any(member => !original.Any(previous => previous[MongoSchema.MemberIdField] == member[MongoSchema.MemberIdField]
            && previous[MongoSchema.MemberHostField] == member[MongoSchema.MemberHostField] && previous[MongoSchema.MemberStateField] == member[MongoSchema.MemberStateField])))
        {
            throw new ComparisonFailureException(MongoSchema.FailureReplicaSetShape);
        }
    }

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
        settings.RetryWrites = false;
        settings.RetryReads = false;
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

    private static async Task VerifyCopiedProbeAsync(IMongoCollection<BsonDocument> primary, IMongoClient[] secondaries, string databaseName,
        int timeoutSeconds, TimeSpan pollInterval, CancellationToken cancellationToken)
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
                await WaitForSecondaryCopyAsync(collection: secondary.GetDatabase(databaseName).GetCollection<BsonDocument>(MongoSchema.DocumentsCollection),
                    probeId: probeId, expectedBody: body, cancellationToken: deadline.Token, pollInterval: pollInterval);
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

    private static async Task WaitForSecondaryCopyAsync(IMongoCollection<BsonDocument> collection, string probeId, string expectedBody,
        TimeSpan pollInterval, CancellationToken cancellationToken)
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
            await Task.Delay(pollInterval, cancellationToken);
        }
    }

    private static ImmutableArray<string> BuildObservations(BsonDocument status, BsonDocument[] members, string version, MongoClientSettings settings)
    {
        const char MemberSeparator = ',';

        var roles = members.Select(member => member.GetValue(MongoSchema.MemberStateField).AsString)
            .Order(StringComparer.Ordinal).ToArray();
        var serverConcern = settings.WriteConcern!.W + MongoSchema.ConcernSeparator + settings.WriteConcern.Journal;
        return [MongoSchema.ObservationMembers + members.Length, MongoSchema.ObservationRoles + string.Join(MemberSeparator, roles),
            MongoSchema.ObservationVersions + version, MongoSchema.ObservationWriteConcern + serverConcern,
            MongoSchema.ObservationReadConcern + settings.ReadConcern!.Level,
            MongoSchema.ObservationReadPreference + settings.ReadPreference!.ReadPreferenceMode,
            MongoSchema.ObservationSet + status[MongoSchema.ReplicaStatusSetField].AsString,
            MongoSchema.ObservationMajority + status[MongoSchema.WriteMajorityField].ToInt32(),
            MongoSchema.ObservationIdentities + string.Join(MemberSeparator, members.Select(member => member[MongoSchema.MemberIdField].ToInt32() + MongoSchema.MemberIdentitySeparator + member[MongoSchema.MemberHostField].AsString)),
            MongoSchema.ObservationSeededCopies];
    }
}
