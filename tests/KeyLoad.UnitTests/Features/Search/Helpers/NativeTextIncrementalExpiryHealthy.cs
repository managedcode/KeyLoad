using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Releases the actual expired original generation and builds a distinct persisted consumer before literal cold continuation.</summary>
internal static class NativeTextIncrementalExpiryHealthy
{
    private const string FreshConsumer = "natural-expiry-healthy-consumer";

    internal static async Task RunAsync(TestDatabase database, TextIndexMaintenanceRequest original, CancellationToken token)
    {
        var releaseId = Guid.NewGuid();
        _ = await NativeTextMaintenanceCommit.ExecuteAsync<ProjectionConsumerInfo>(database, OperationKind.ReleaseProjectionConsumer,
            new ReleaseProjectionConsumerRequest(releaseId, original.Consumer, original.IndexGeneration), releaseId, token);
        await NativeTextIncrementalIntentOwner.RunAsync(database, async runtime =>
        {
            _ = await runtime.PhaseAsync(database, original with { CommandId = Guid.NewGuid(), Mode = TextIndexMaintenanceMode.Release },
                TextMaintenanceCapabilityKind.Release, token: token);
        });
        var fresh = original with { CommandId = Guid.NewGuid(), Consumer = new(original.Consumer.Partition, FreshConsumer) };
        var configureId = Guid.NewGuid();
        _ = await NativeTextMaintenanceCommit.ExecuteAsync<ProjectionConsumerInfo>(database, OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(configureId, fresh.Consumer,
                new(fresh.IndexGeneration, [fresh.Collection], [MutationDiscriminatorNames.PutDocument,
                    MutationDiscriminatorNames.PatchDocument, MutationDiscriminatorNames.DeleteDocument])), configureId, token);
        await NativeTextIncrementalIntentOwner.RunAsync(database, async runtime =>
        {
            var completed = await NativeTextMaintenancePhaseFlow.FinishAsync(database, runtime, fresh, token);
            await Assert.That(completed.Checkpoint).IsEqualTo(completed.ThroughSequence);
            await NativeTextIncrementalIntentContinuation.LiteralAsync(database, runtime, fresh, token);
        });
        await NativeTextIncrementalIntentOwner.RunAsync(database, async cold =>
        {
            _ = await cold.PhaseAsync(database, fresh, TextMaintenanceCapabilityKind.Begin, token: token);
            await NativeTextIncrementalIntentContinuation.LiteralAsync(database, cold, fresh, token);
        });
    }
}
