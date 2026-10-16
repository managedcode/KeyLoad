using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Joins both actual native owners before two cold original-entry and terminal-outcome replays.</summary>
internal static class ResourceLifetimeGrantCold
{
    private const int FirstCold = 0;
    private const int ColdContinuations = 2;
    private const int SingleEntry = 1;
    private const long SequenceStep = 1;

    internal static async Task ExecuteAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        var source = state.Source;
        var target = state.Target;
        var original = state.Original;
        var first = ResourceLifetimeGrantAssertions.Resolve(source, original, token);
        await Assert.That(first.Error).IsNull();
        var receipt = first.Get<CommitReceipt>();
        var entry = target.Journal.Log.Read(checked(state.TargetIndex + SequenceStep), SingleEntry,
            state.TargetOptions.ReplicaConfiguration.Value.MaxAppendBytes).Single();
        var effectOperation = entry.Operation ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var originalEffect = target.Database.ResolveOutcome(effectOperation);
        await Assert.That(originalEffect.Error).IsNull();
        var authenticated = PartitionMovementNativePayload.ReadAuthenticated(effectOperation);
        await ResourceLifetimeGrantAssertions.EqualAsync(state.Proof, authenticated.LifetimeProofs.Single());
        await HealthyMetadataAsync(state, token);
        var sourceState = ControlledPartitionMovementExpiryOwnerState.Capture(source);
        var targetState = ControlledPartitionMovementExpiryOwnerState.Capture(target);
        for (var pass = FirstCold; pass < ColdContinuations; pass++)
        {
            source.Reopen();
            target.Reopen();
            await sourceState.AssertUnchangedAsync(source);
            await targetState.AssertUnchangedAsync(target);
            await ResourceLifetimeGrantAssertions.EqualAsync(first,
                ResourceLifetimeGrantAssertions.Resolve(source, original, token));
            var replay = target.Journal.Submit(effectOperation, token);
            await ResourceLifetimeGrantAssertions.EqualAsync(originalEffect, replay);
            await ResourceLifetimeGrantAssertions.EqualAsync(originalEffect.Get<PartitionMovePhaseResult>().ControlledEffect!.OriginalResult,
                replay.Get<PartitionMovePhaseResult>().ControlledEffect!.OriginalResult);
            await ResourceLifetimeGrantAssertions.EqualAsync(state.Proof,
                source.Database.CaptureControlledCommandLifetimeProof(state.Request.Envelope, token));
            await ResourceLifetimeGrantAssertions.DocumentAsync(source, target, receipt.Token, token);
            await sourceState.AssertUnchangedAsync(source);
            await targetState.AssertUnchangedAsync(target);
            await HealthyMetadataAsync(state, token);
            sourceState = ControlledPartitionMovementExpiryOwnerState.Capture(source);
        }
    }

    private static async Task HealthyMetadataAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        var source = state.Source;
        var partition = state.Context.Control.Partition;
        var definition = state.Record.Delegation!.Resources.Single(value => value.Name == ControlledPartitionMovementCorpus.Collection);
        var key = ResourceLifetimeCatalog.Key(partition.TenantId, partition.DatabaseId, definition.Name);
        var lifetime = source.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        var original = source.Database.CreateNativeOperation(OperationKind.ConfigureResource, Guid.NewGuid(), "root",
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var result = source.Journal.Submit(original, token);
        await Assert.That(result.Error).IsNull();
        await ResourceLifetimeGrantAssertions.EqualAsync(definition, result.Get<ResourceDefinition>());
        await Assert.That(source.Store.Read(view => view.ReadOwnedValue(key))!.SequenceEqual(lifetime)).IsTrue();
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(source);
        foreach (var attempt in state.MetadataAttempts)
        {
            await ResourceLifetimeGrantAssertions.EqualAsync(attempt.Result, source.Journal.Submit(attempt.Original, token));
        }
        await ResourceLifetimeGrantAssertions.EqualAsync(result, source.Journal.Submit(original, token));
        await before.AssertUnchangedAsync(source);
        await target.AssertUnchangedAsync(state.Target);
    }
}
