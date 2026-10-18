using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Reads only committed production authority; no synthetic directory, fence or work acknowledgement.</summary>
internal static class NativeDropPreparedPageProducer
{
    internal static async Task<ResourceRetirementStateV1> CaptureAsync(NativeDropRuntimeProfile profile,
        ResourceRetirementDropParentLookupV1 lookup, NativeDropCommandPhaseGate intent,
        NativeDropPublicCall call, CancellationToken token)
    {
        await WaitAsync(intent, call.Original, token).ConfigureAwait(false);
        await profile.Source.Partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        var parent = profile.Source.Database.ReadResourceRetirementDropParent(PartitionStoreProtocol.AdministratorId,
            lookup, Work(profile, token)) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, NativeDropRuntimeProtocol.MissingReply);
        var workId = ResourceRetirementIdentity.PhaseCommandId(parent.RetirementRequest,
            ResourceRetirementPhase.SealAllWork, profile.Hosts.Control.Owner.PhysicalShardId);
        var joined = profile.Observer.Hold(workId);
        intent.Released.TrySetResult();
        await WaitAsync(joined, call.Original, token).ConfigureAwait(false);
        await intent.Disposed.Task.WaitAsync(token).ConfigureAwait(false);
        await profile.Source.Partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        var state = profile.Source.Database.ReadResourceRetirementState(PartitionStoreProtocol.AdministratorId,
            parent.RetirementRequest, Work(profile, token));
        var original = profile.Source.Database.ReadResourceRetirementOriginalWorkSeal(PartitionStoreProtocol.AdministratorId,
            state.Control, Work(profile, token));
        await NativeDropRuntimeAssertions.GenuineWorkSealAsync(original, profile).ConfigureAwait(false);
        return original;
    }

    private static async Task WaitAsync(NativeDropCommandPhaseGate hold, Task<GrainOperationReply> original,
        CancellationToken token)
    {
        _ = await Task.WhenAny(hold.Arrived.Task, original).WaitAsync(token).ConfigureAwait(false);
        if (hold.Arrived.Task.IsCompletedSuccessfully)
        {
            return;
        }

        _ = NativeDropRuntimeAssertions.Value<DropTableResult>(await original.ConfigureAwait(false));
        throw new InvalidOperationException(NativeDropRuntimeProtocol.MissingHold);
    }

    internal static ReadExecutionBudget Work(NativeDropRuntimeProfile profile, CancellationToken token)
        => new(profile.Source.Database.OperationLimitsOptions, profile.Source.Clock, token);
}
