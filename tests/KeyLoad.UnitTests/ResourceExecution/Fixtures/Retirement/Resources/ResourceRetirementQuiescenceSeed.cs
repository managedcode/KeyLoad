using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementQuiescenceData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescenceSeed
{
    internal static async Task<ResourceRetirementQuiescenceState> ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus, CancellationToken token)
    {
        var seeded = await ControlledPartitionMovementPrepareSeed.ExecuteAsync(source, target, corpus, token);
        ResourceRetirementQuiescenceState? state = null;
        await ResourceRetirementQuiescenceAdmissions.RunAsync(source, target, corpus,
            async (sourceRuntime, targetRuntime, sourceAdmission, targetAdmission) =>
            {
                var caller = ControlledPartitionMovementPrepareRequest.CallerAddress(listeners);
                var prepared = await ControlledPartitionMovementPrepareFlow.ExecuteAsync(source, target, corpus,
                    sourceRuntime, sourceAdmission, caller, token);
                var fenced = await ControlledPartitionMovementGrantFlow.ExecuteAsync(source, sourceRuntime,
                    sourceAdmission, corpus, prepared.Prepared, caller, prepared.ExpiresAt, token);
                var accepted = await ControlledPartitionMovementFenceSettlementFlow.ExecuteAsync(source, sourceRuntime,
                    sourceAdmission, corpus, prepared.Prepared, fenced.Authorization, fenced.Fence, caller, prepared.ExpiresAt, token);
                var lifetime = ResourceRetirementQuiescencePhases.ReadLifetime(source);
                var request = new ResourceRetirementRequestV1(ResourceRetirementQuiescenceData.Version, RetirementId, Partition.TenantId,
                    Partition.DatabaseId, lifetime.ResourceName, lifetime.Incarnation);
                state = new(source, target, listeners, corpus, accepted, prepared.ExpiresAt, request,
                    seeded.Receipt, seeded.Blob, seeded.RecordedAt, seeded.Authority);
                var acceptedOriginal = source.Journal.Log.Read(accepted.Journal.AppliedPosition, One,
                    source.Journal.Configuration.MaxAppendBytes).Single().Operation ?? throw new InvalidOperationException(Invalid);
                var acceptedOutcome = source.Database.ResolveOutcome(acceptedOriginal);
                await Assert.That(acceptedOutcome.Error).IsNull();
                await ResourceRetirementQuiescenceAssertions.EqualAsync(accepted, acceptedOutcome.Get<PartitionMovePhaseResult>());
                state.Originals.Add((source, acceptedOriginal, acceptedOutcome));
            });
        return state ?? throw new InvalidOperationException(Invalid);
    }
}
