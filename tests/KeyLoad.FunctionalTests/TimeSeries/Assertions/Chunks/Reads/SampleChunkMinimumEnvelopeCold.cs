using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.FunctionalTests.OrleansExecution;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class SampleChunkMinimumEnvelopeCold
{
    private const int FirstWave = 0;
    private const int ColdWaves = 2;
    private const int LocalPersistedCopies = 1;

    internal static async Task RunAsync(ConnectionColdSource owner, SampleChunkMinimumEnvelopeState state,
        SampleRawIntegrityCut expected, CancellationToken token)
    {
        for (var wave = FirstWave; wave < ColdWaves; wave++)
        {
            var failures = new List<Exception>();
            PartitionStores? stores = null;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                stores = owner.OpenStores();
                var reopened = stores.Canonical;
                await SampleRawIntegrityAssertions.UnchangedAsync(reopened, expected);
                var database = owner.CreateDatabase(reopened);
                var codec = new GrainRequestCodec(database.Store.Identity.Incarnation, owner.Clock, owner.RoutingOptions);
                await SampleChunkMinimumEnvelopeAssertions.ObserveAsync(reopened, () =>
                {
                    var id = Guid.NewGuid();
                    var issued = SampleChunkMinimumEnvelopeIssuance.Create(codec, state.Reader, id, state.Request);
                    var decoded = SampleChunkMinimumEnvelopeIssuance.Decode(codec, issued, id, state.Request);
                    SampleChunkMinimumEnvelopeAssertions.Page(state.Request,
                        database.ReadSampleChunkWindow(state.Reader.Id, decoded, token));
                    return Task.CompletedTask;
                });
                await ReplayAsync(database, reopened, state, token);
            }, failures);
            if (stores is { } acquired)
            {
                ServerFailureObserver.Observe(acquired.Dispose, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        SampleChunkMinimumEnvelopeState state, CancellationToken token)
    {
        foreach (var original in state.Commands)
        {
            await SampleChunkMinimumEnvelopeAssertions.ObserveAsync(store, () =>
            {
                var operation = database.CreateCommand(OperationKind.Batch, original.Request.CommandId, SampleChunkCanonicalFixture.Principal, default, JsonSerializer.Serialize(original.Request, JsonDefaults.Options));
                var replay = database.ApplyEmbedded(operation, cancellationToken: token).Get<CommitReceipt>();
                SampleChunkExplicitMinimumUnitReads.RequireBytes(original.Receipt with { PersistedCopies = LocalPersistedCopies }, replay);
                return Task.CompletedTask;
            });
        }
    }
}
