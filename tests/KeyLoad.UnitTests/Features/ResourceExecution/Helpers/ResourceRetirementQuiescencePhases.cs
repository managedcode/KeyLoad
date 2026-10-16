using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementQuiescenceData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescencePhases
{
    internal static long Epoch(ControlledPartitionMovementNode owner)
        => owner.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(Root)))?.PolicyEpoch
            ?? throw new InvalidOperationException(Invalid);

    internal static ReadExecutionBudget Work(ControlledPartitionMovementNode owner, CancellationToken token)
        => new(UnitExecutionOptions.DatabaseLimits(owner.Database.Limits), owner.Database.EvaluationClock, token);

    internal static async Task<ResourceRetirementPhaseResultV1> SubmitAsync<T>(ResourceRetirementQuiescenceState state,
        ControlledPartitionMovementNode owner, ResourceRetirementPhase phase, Guid id, T body, CancellationToken token)
    {
        var index = owner.Journal.Log.State.LastIndex + One;
        var command = new ResourceRetirementPhaseCommandV1(ResourceRetirementQuiescenceData.Version, state.Request.RetirementId, phase,
            NativeSerialization.Serialize(body));
        var original = owner.Database.CreateResourceRetirementOperation(id, Root,
            owner.Database.EvaluationClock.GetUtcNow(), command);
        var outcome = owner.Journal.Submit(original, token);
        await Assert.That(outcome.Error).IsNull();
        var actual = outcome.Get<ResourceRetirementPhaseResultV1>();
        await ResourceRetirementQuiescenceAssertions.EqualAsync(new ResourceRetirementPhaseReceiptV1(ResourceRetirementQuiescenceData.Version,
            state.Request.RetirementId, phase, id, state.Control?.Lifetime ?? ReadLifetime(owner),
            owner.PhysicalOwner, index, state.Control is null ? NativeOperationFingerprint.Compute(original) : state.Control.IntentDigest), actual.Receipt);
        state.Originals.Add((owner, original, outcome));
        return actual;
    }

    internal static ResourceLifetimeV1 ReadLifetime(ControlledPartitionMovementNode owner)
        => owner.Store.Read(view =>
        {
            var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(Partition.TenantId,
                Partition.DatabaseId, ControlledPartitionMovementCorpus.Collection));
            return ResourceLifetimeCatalog.Read(view, Partition.TenantId, Partition.DatabaseId,
                ControlledPartitionMovementCorpus.Collection, definition) ?? throw new InvalidOperationException(Invalid);
        });

}
