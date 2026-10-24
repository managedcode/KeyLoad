using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Search;

/// <summary>Releases the actual expired original generation and builds a distinct persisted consumer before literal cold continuation.</summary>
internal static class TextIncrementalExpiryHealthy
{
    private const string FreshConsumer = "natural-expiry-healthy-consumer";

    internal static async Task RunAsync(TestDatabase database, TextIndexMaintenanceRequest original, CancellationToken token)
    {
        var releaseId = Guid.NewGuid();
        _ = await TextMaintenanceCommit.ExecuteAsync<ProjectionConsumerInfo>(database, OperationKind.ReleaseProjectionConsumer,
            new ReleaseProjectionConsumerRequest(releaseId, original.Consumer, original.IndexGeneration), releaseId, token);
        await TextIncrementalIntentOwner.RunAsync(database, async runtime =>
        {
            _ = await runtime.PhaseAsync(database, original with
            {
                CommandId = Guid.NewGuid(),
                Mode = TextIndexMaintenanceMode.Release
            },
                TextMaintenanceCapabilityKind.Release, token: token);
        });
        var fresh = original with
        {
            CommandId = Guid.NewGuid(),
            Consumer = new(original.Consumer.Partition, FreshConsumer)
        };
        var configureId = Guid.NewGuid();
        _ = await TextMaintenanceCommit.ExecuteAsync<ProjectionConsumerInfo>(database, OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(configureId, fresh.Consumer,
                new(fresh.IndexGeneration, [fresh.Collection], [MutationDiscriminatorNames.PutDocument,
                    MutationDiscriminatorNames.PatchDocument, MutationDiscriminatorNames.DeleteDocument])), configureId, token);
        await TextIncrementalIntentOwner.RunAsync(database, async runtime =>
        {
            var completed = await TextMaintenancePhaseFlow.FinishAsync(database, runtime, fresh, token);
            await Assert.That(completed.Checkpoint).IsEqualTo(completed.ThroughSequence);
            await TextIncrementalIntentContinuation.LiteralAsync(database, runtime, fresh, token);
        });
        await TextIncrementalIntentOwner.RunAsync(database, async cold =>
        {
            _ = await cold.PhaseAsync(database, fresh, TextMaintenanceCapabilityKind.Begin, token: token);
            await TextIncrementalIntentContinuation.LiteralAsync(database, cold, fresh, token);
        });
    }
}
