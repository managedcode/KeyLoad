using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Observation transitions under the existing owner's borrowed admission gate and table.</summary>
internal sealed class NativeCommandObservationGate(Lock gate, NativeRequestWorkOwner owner,
    NativeResourceWorkPhysicalBinding physical, NativeRequestWorkAdmissions admissions)
{
    internal NativeRequestWorkLease AcquireCommandObservation(Guid id, NativeRequestWorkLease command,
        PhysicalShardRecord actualOwner, SiloStorageIdentity actualRuntime, DatabaseEngine database,
        NativeCommandObservationResources resources)
    {
        lock (gate)
        {
            physical.RequireActual(actualOwner, actualRuntime);
            physical.RequireDatabase(database);
            admissions.RequireActive(command);
            command.RequirePublicationOpen();
            var dependencies = command.Dependencies.ToImmutableArray();
            resources.ResultRetention.Reserve(dependencies, resources.MaximumResultBytes);
            var lease = owner.Acquire(id, NativeRequestWorkKind.ReadCapability);
            lease.AttachResultRetention(resources.ResultRetention);
            lease.Dependencies = command.Dependencies;
            return lease;
        }
    }

    internal void AttachIntent(NativeCommandObservation observation, ReplicatedOperation intent,
        NativeCapabilityWorkLifetime commandWork)
    {
        var command = commandWork.OriginalProducer;
        lock (gate)
        {
            admissions.RequireActive(observation.Lease);
            admissions.RequireActive(command);
            observation.Lease.RequirePublicationOpen();
            command.RequirePublicationOpen();
            observation.RequireIntentMissingAtGate(command);
            var dependencies = command.Dependencies.ToImmutableArray();
            observation.Lease.ResultRetention.Reserve(dependencies);
            admissions.Bind(observation.Lease, dependencies);
            observation.AttachIntentAtGate(intent, commandWork);
        }
    }

    internal NativeCommandObservation ConsumeCommandObservation(NativeCommandObservationHandleV1 handle,
        PhysicalShardRecord actualOwner, SiloStorageIdentity actualRuntime, DatabaseEngine database)
    {
        lock (gate)
        {
            physical.RequireActual(actualOwner, actualRuntime);
            physical.RequireDatabase(database);
            var retained = admissions.FindObservation(handle, actualOwner, actualRuntime, database);
            retained.Lease.RequirePublicationOpen();
            retained.ConsumeAtGate();
            return retained;
        }
    }

    internal NativeCommandObservation BeginCommandExecution(NativeCommandObservationHandleV1 handle,
        PhysicalShardRecord actualOwner, SiloStorageIdentity actualRuntime, DatabaseEngine database)
    {
        lock (gate)
        {
            physical.RequireActual(actualOwner, actualRuntime);
            physical.RequireDatabase(database);
            var retained = admissions.FindObservation(handle, actualOwner, actualRuntime, database);
            retained.Lease.RequirePublicationOpen();
            admissions.RequireActive(retained.CommandWork.OriginalProducer);
            retained.CommandWork.OriginalProducer.RequirePublicationOpen();
            retained.BeginCommandAtGate();
            return retained;
        }
    }

    internal Task AbortCommandObservationByHandleAsync(NativeCommandObservationHandleV1 handle,
        PhysicalShardRecord actualOwner, SiloStorageIdentity actualRuntime, DatabaseEngine database)
    {
        NativeCommandObservation retained;
        lock (gate)
        {
            physical.RequireActual(actualOwner, actualRuntime);
            physical.RequireDatabase(database);
            retained = admissions.FindObservation(handle, actualOwner, actualRuntime, database);
        }
        return retained.AbortAsync(null);
    }

    internal Task FinishCommandExecutionAsync(NativeCommandObservation observation, Exception? original)
    {
        Task task;
        TaskCompletionSource? start;
        lock (gate)
        {
            (task, start) = observation.SelectCommandCompletionAtGate(original);
        }
        start?.TrySetResult();
        return task;
    }

    internal bool MarkSubmitted(NativeCommandObservation observation)
    {
        lock (gate)
        {
            return observation.MarkSubmittedAtGate();
        }
    }

    internal void MarkCommandObservationSettled(NativeCommandObservation observation)
    {
        lock (gate)
        {
            observation.MarkSettledAtGate();
        }
    }

    internal Task SettleCommandObservation(NativeCommandObservation observation,
        Exception? original, Exception? cleanup, bool abort)
    {
        TaskCompletionSource? start;
        Task task;
        lock (gate)
        {
            (task, start) = observation.SelectSettlementAtGate(original, cleanup, abort);
        }
        start?.TrySetResult();
        return task;
    }

}
