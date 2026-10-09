using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal sealed class DocumentMongoClient : IAsyncDisposable
{
    private MongoClient? actual;
    private MongoSession? session;
    private Exception? primary;
    internal static async Task<IDocumentComparisonSession> OpenAsync(MongoTarget target, MongoClientSettings settings,
        string databaseName, int graphDepth, IOptions<NativeComparisonExecutionOptions> options, CancellationToken token)
    {
        await using var owner = new DocumentMongoClient();
        try
        { return await owner.AcquireAsync(target, settings, databaseName, graphDepth, options, token).ConfigureAwait(false); }
        catch (Exception failure) { owner.primary = failure; throw; }
    }
    private async Task<IDocumentComparisonSession> AcquireAsync(MongoTarget target, MongoClientSettings settings,
        string databaseName, int graphDepth, IOptions<NativeComparisonExecutionOptions> options, CancellationToken token)
    {
        var identity = Guid.NewGuid();
        settings.ApplicationName = DocumentProtocolText.KeyloadDocument + identity.ToString(DocumentProtocolText.N);
        actual = new(settings);
        var database = actual.GetDatabase(databaseName);
        _ = await database.RunCommandAsync<BsonDocument>(new BsonDocument(DocumentProtocolText.Ping, DocumentMeasurementValues.SingleItemCount), cancellationToken: token).ConfigureAwait(false);
        session = Session(target, database, graphDepth, options);
        var result = new DocumentOwnedSession(session, identity, session.ReadCorpusAsync, () => ValueTask.CompletedTask, nativeClient: actual);
        actual = null;
        session = null;
        return result;
    }
    public async ValueTask DisposeAsync()
    {
        var cleanup = new List<Exception>();
        if (session is not null && await OpenLoopFailure.ObserveAsync(DisposeSessionAsync()).ConfigureAwait(false) is { } sessionFailure)
        {
            cleanup.Add(sessionFailure);
        }

        if (actual is not null && await OpenLoopFailure.ObserveAsync(DisposeClientAsync()).ConfigureAwait(false) is { } clientFailure)
        {
            cleanup.Add(clientFailure);
        }

        var combined = OpenLoopFailure.Combine(primary, [.. cleanup]);
        if (cleanup.Count != DocumentMeasurementValues.NoObservedItems && combined is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(combined).Throw();
        }
    }
    private async Task DisposeSessionAsync() => await session!.DisposeAsync().ConfigureAwait(false);
    private async Task DisposeClientAsync() { await Task.CompletedTask.ConfigureAwait(false); actual!.Dispose(); }
    internal static async Task VerifyCopiesAsync(MongoTarget target, IEnumerable<IMongoClient> clients,
        string databaseName, int graphDepth, IOptions<NativeComparisonExecutionOptions> options,
        DocumentComparisonSchedule schedule, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        foreach (var actual in clients)
        {
            MongoSession? session = null;
            try
            {
                session = Session(target, actual.GetDatabase(databaseName), graphDepth, options);
                _ = await DocumentComparisonOracle.VerifyAsync(session.ReadCorpusAsync(token), schedule.FinalDocuments(), token).ConfigureAwait(false);
            }
            finally
            {
                if (session is not null)
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
    private static MongoSession Session(MongoTarget target, IMongoDatabase database, int graphDepth,
        IOptions<NativeComparisonExecutionOptions> options) => new(target,
            database.GetCollection<BsonDocument>(MongoSchema.DocumentsCollection), database.GetCollection<BsonDocument>(MongoSchema.EdgesCollection),
            database.GetCollection<BsonDocument>(MongoSchema.EventsCollection), graphDepth, DocumentMeasurementValues.NativeActualEnumeration, options);
}
