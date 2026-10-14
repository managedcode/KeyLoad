using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkSignedMinimumCold
{
    private const int FirstWave = 0;
    private const int ColdWaves = 2;

    internal static async Task RunAsync(TestDatabase owner, SampleChunkSignedMinimumState state,
        RequestCqrsClusterFixture fixture, SampleRawIntegrityCut expected, CancellationToken token)
    {
        for (var wave = FirstWave; wave < ColdWaves; wave++)
        {
            var failures = new List<Exception>();
            ZoneTreeStore? reopened = null;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                reopened = new(new(owner.Directory), owner.StorageExecution, UnitExecutionOptions.PointCacheExecution());
                await SampleRawIntegrityAssertions.UnchangedAsync(reopened, expected);
                var database = owner.CreateConfiguredDatabase(reopened);
                var codec = new GrainRequestCodec(database, fixture.Clock, fixture.RoutingOptions);
                await SampleChunkSignedMinimumAssertions.ObserveAsync(reopened, () =>
                {
                    var id = Guid.NewGuid();
                    var signed = SampleChunkSignedMinimumEnvelope.Create(codec, state.Reader, id, state.Request);
                    var decoded = SampleChunkSignedMinimumEnvelope.Decode(codec, signed, id, state.Request);
                    SampleChunkSignedMinimumAssertions.Page(state.Request,
                        database.ReadSampleChunkWindow(state.Reader.Id, decoded, token));
                    return Task.CompletedTask;
                });
                await ReplayAsync(database, reopened, state, token);
            }, failures);
            if (reopened is { } acquired)
            {
                ServerFailureObserver.Observe(acquired.Dispose, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        SampleChunkSignedMinimumState state, CancellationToken token)
    {
        foreach (var original in state.Commands)
        {
            await SampleChunkSignedMinimumAssertions.ObserveAsync(store, () =>
            {
                var operation = new ReplicatedOperation(original.Request.CommandId, OperationKind.Batch,
                    SampleChunkCanonicalFixture.Principal, default, JsonSerializer.Serialize(original.Request, JsonDefaults.Options));
                var replay = database.ApplyEmbedded(operation, cancellationToken: token).Get<CommitReceipt>();
                SampleChunkExplicitMinimumUnitReads.RequireBytes(original.Receipt, replay);
                return Task.CompletedTask;
            });
        }
    }
}
