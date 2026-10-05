using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Runs the comparison workload against an owned MongoDB database.</summary>
/// <param name="connectionString">MongoDB connection string for the selected topology.</param>
/// <param name="runId">Stable run identifier used to isolate the benchmark database.</param>
/// <param name="image">Server image recorded in comparison provenance.</param>
/// <param name="topology">Requested one, two or three native members.</param>
/// <param name="lifecycleOptions">Centrally validated native lifecycle policy.</param>
public sealed class MongoTarget(string connectionString, string runId, string image, ComparisonTopology topology,
    IOptions<ComparisonLifecycleOptions> lifecycleOptions) : IComparisonTarget
{
    private readonly string databaseName = MongoSchema.DatabasePrefix + Guid.Parse(runId).ToString(MongoSchema.InvariantFormat);
    private readonly List<IMongoClient> ownedClients = [];
    private MongoClient? primaryClient;
    private IMongoDatabase? database;
    private IMongoCollection<BsonDocument>? documents;
    private IMongoCollection<BsonDocument>? edges;
    private IMongoCollection<BsonDocument>? events;
    private int graphDepth;
    private int corpusCount;

    /// <summary>Gets the verified server profile after initialization.</summary>
    public TargetProfile Profile { get; private set; } = MongoProfileFactory.Create(connectionString, image, topology);

    /// <summary>Reports whether the requested workload scenario has a MongoDB implementation.</summary>
    /// <param name="scenario">The comparison scenario.</param>
    /// <returns>Whether this target supports that scenario.</returns>
    public bool Supports(Scenario scenario)
        => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete or Scenario.GraphNeighbors or Scenario.GraphTraverse
            or Scenario.StreamAppend or Scenario.StreamRead;

    /// <summary>Creates collections, seeds the corpus, and verifies the selected topology.</summary>
    /// <param name="dataset">The deterministic comparison corpus and options.</param>
    /// <param name="cancellationToken">Cancels initialization.</param>
    /// <returns>A task that completes after verification.</returns>
    public async Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        graphDepth = dataset.Settings.GraphDepth;
        if (dataset.Settings is ScaledComparisonProfile)
        {
            Profile = Profile with { ReadContract = "primary majority document reads; S1 seeds documents only, with no edge or event records" };
        }
        corpusCount = dataset.Documents.Count;
        var settings = CreateSettings(connectionString, dataset.Settings.Concurrency);
        primaryClient = new MongoClient(settings);
        ownedClients.Add(primaryClient);
        database = primaryClient.GetDatabase(databaseName);
        documents = database.GetCollection<BsonDocument>(MongoSchema.DocumentsCollection);
        edges = database.GetCollection<BsonDocument>(MongoSchema.EdgesCollection);
        events = database.GetCollection<BsonDocument>(MongoSchema.EventsCollection);

        await CreateIndexesAsync(cancellationToken);
        await MongoCorpusSeed.SeedAsync(dataset, documents, edges, events, StreamName, cancellationToken);
        if (dataset.Settings is not ScaledComparisonProfile)
        {
            await VerifyUniqueStreamInsertionAsync(cancellationToken);
        }
        var version = await ReadPrimaryVersionAsync(cancellationToken);
        var profile = Profile with { Version = version };
        if (ComparisonTopologies.NodeCount(topology) > 1)
        {
            var proof = await MongoReplicaVerifier.VerifyAsync(connectionString,
                primaryClient.GetDatabase(MongoSchema.AdminDatabase), database, documents,
                topology, dataset, cancellationToken, lifecycleOptions);
            ownedClients.AddRange(proof.SecondaryClients);
            profile = profile with { Cluster = proof.Evidence, Version = proof.Version };
        }
        else
        {
            await VerifySingleNodeAsync(cancellationToken);
            profile = profile with
            {
                Cluster = new ClusterEvidence(MongoSchema.SingleNodeCount, MongoSchema.SingleNodeCount,
                MongoSchema.SingleState, [MongoSchema.SingleObservation])
            };
        }
        Profile = profile;
    }

    /// <summary>Opens a session over this initialized target.</summary>
    /// <param name="cancellationToken">Cancellation token for the session request.</param>
    /// <returns>A session that borrows this target's MongoDB collections.</returns>
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
    {
        if (documents is null || edges is null || events is null)
        {
            throw new ComparisonFailureException(MongoSchema.FailureNotInitialized);
        }
        return Task.FromResult<IComparisonSession>(new MongoSession(this, documents, edges, events, graphDepth, corpusCount));
    }

    /// <summary>Drops the isolated benchmark database and disposes owned clients.</summary>
    /// <returns>A task that completes when cleanup finishes.</returns>
    public async ValueTask DisposeAsync()
    {
        if (primaryClient is null)
        {
            return;
        }
        using var cleanup = new CancellationTokenSource(lifecycleOptions.Value.MongoCleanupTimeout);
        try
        {
            await primaryClient.DropDatabaseAsync(databaseName, cleanup.Token);
        }
        finally
        {
            foreach (var client in ownedClients)
            {
                if (ReferenceEquals(client, primaryClient))
                {
                    primaryClient.Dispose();
                }
                else
                {
                    client.Dispose();
                }
            }
            ownedClients.Clear();
        }
    }

    internal string StreamName(BenchmarkDocument document)
        => MongoSchema.DatabasePrefix + databaseName + MongoSchema.DatabaseSeparator + document.Id;

    internal static MongoClientSettings CreateSettings(string connectionString, int concurrency)
    {
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        settings.WriteConcern = MongoSchema.MajorityJournalWriteConcern;
        settings.ReadConcern = ReadConcern.Majority;
        settings.ReadPreference = ReadPreference.Primary;
        settings.RetryWrites = false;
        settings.RetryReads = false;
        settings.MaxConnectionPoolSize = Math.Max(concurrency + MongoPool.SessionMargin, MongoPool.MinimumSize);
        return settings;
    }

    private async Task CreateIndexesAsync(CancellationToken cancellationToken)
    {
        var edgeCollection = Require(edges);
        var sourceIndex = new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending(MongoSchema.FromField));
        await edgeCollection.Indexes.CreateOneAsync(sourceIndex, cancellationToken: cancellationToken);
    }

    private async Task VerifySingleNodeAsync(CancellationToken cancellationToken)
    {
        var admin = primaryClient!.GetDatabase(MongoSchema.AdminDatabase);
        var hello = await admin.RunCommandAsync<BsonDocument>(
            new BsonDocument(MongoSchema.HelloCommand, MongoSchema.CommandEnabledValue),
            ReadPreference.Primary, cancellationToken);
        if (!hello.GetValue(MongoSchema.WritablePrimaryField, false).ToBoolean()
            || hello.GetValue(MongoSchema.RouterMessageField, BsonNull.Value) == MongoSchema.RouterMessage)
        {
            throw new ComparisonFailureException(MongoSchema.FailureUnsupportedTopology);
        }
        if (!hello.Contains(MongoSchema.ReplicaSetNameField))
        {
            return;
        }
        var status = await admin.RunCommandAsync<BsonDocument>(
            new BsonDocument(MongoSchema.ReplicaSetStatusCommand, MongoSchema.CommandEnabledValue),
            ReadPreference.Primary, cancellationToken);
        var members = status.GetValue(MongoSchema.MembersField).AsBsonArray;
        if (members.Count != MongoSchema.SingleNodeCount
            || members[0].AsBsonDocument.GetValue(MongoSchema.MemberStateField).AsString != MongoSchema.PrimaryState
            || members[0].AsBsonDocument.GetValue(MongoSchema.MemberHealthField).ToInt32() != MongoSchema.HealthyMemberValue)
        {
            throw new ComparisonFailureException(MongoSchema.FailureUnsupportedTopology);
        }
    }

    private async Task<string> ReadPrimaryVersionAsync(CancellationToken cancellationToken)
    {
        var result = await Require(database).RunCommandAsync<BsonDocument>(
            new BsonDocument(MongoSchema.BuildInfoCommand, MongoSchema.CommandEnabledValue),
            ReadPreference.Primary, cancellationToken);
        if (!result.TryGetValue(MongoSchema.VersionField, out var version) || !version.IsString)
        {
            throw new ComparisonFailureException(MongoSchema.FailureVersionUnavailable);
        }
        return version.AsString;
    }

    private async Task VerifyUniqueStreamInsertionAsync(CancellationToken cancellationToken)
    {
        var streamId = MongoSchema.CollectionProbePrefix + Guid.NewGuid().ToString(MongoSchema.InvariantFormat);
        var collection = Require(events);
        await collection.InsertOneAsync(MongoSchema.Event(streamId, Guid.NewGuid(), MongoProbe.Payload), cancellationToken: cancellationToken);
        try
        {
            await collection.InsertOneAsync(MongoSchema.Event(streamId, Guid.NewGuid(), MongoProbe.Payload), cancellationToken: cancellationToken);
            throw new ComparisonFailureException(MongoSchema.FailureDuplicateStreamAccepted);
        }
        catch (MongoWriteException error) when (error.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // A different event ID still conflicts because this adapter promises one event per unique stream.
        }
        finally
        {
            await collection.DeleteOneAsync(new BsonDocument(MongoSchema.IdField, streamId), cancellationToken: cancellationToken);
        }
    }

    private static T Require<T>(T? value) where T : class
        => value ?? throw new ComparisonFailureException(MongoSchema.FailureNotInitialized);

}

internal static class MongoPool
{
    public const int SessionMargin = 4;
    public const int MinimumSize = 16;
}

internal static class MongoProbe
{
    public const string Payload = "{\"unique\":true}";
}
