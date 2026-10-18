using ManagedCode.Communication;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class AtomicTriadStagedWarm
{
    internal static async Task RunAsync(AtomicTriadStagedFixture state, List<Exception> failures)
    {
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedReplay.CancelAsync(state), failures);
        await ServerFailureObserver.ObserveAsync(() => FailedAsync(state), failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedReplay.RunAsync(state, false), failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedCurrent.EqualAsync(
            state.SetPolicy(AtomicTriadStagedProtocol.WithdrawnEpoch, false),
            AtomicTriadStagedRecipe.Principal(state.Partition, AtomicTriadStagedProtocol.WithdrawnEpoch, false)), failures);
        await ServerFailureObserver.ObserveAsync(() => DeniedAsync(state), failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedCurrent.EqualAsync(
            state.SetPolicy(AtomicTriadStagedProtocol.RepairedEpoch, true),
            AtomicTriadStagedRecipe.Principal(state.Partition, AtomicTriadStagedProtocol.RepairedEpoch, true)), failures);
        await ServerFailureObserver.ObserveAsync(() => HealthyAsync(state), failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedCurrent.RequireProducedAsync(state), failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedReplay.RunAsync(state, true), failures);
    }
    private static async Task FailedAsync(AtomicTriadStagedFixture state)
    {
        var before = AtomicTriadOutboxImage.Domain(state.Store, state.Partition);
        var operation = state.Operation(state.Failed);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var result = state.Owner.ApplyEmbedded(operation, TestContext.Current!.Execution.CancellationToken);
            ServerFailureObserver.Observe(() => AtomicTriadStagedReplay.Retain(state, operation, result), failures);
            await AtomicTriadStagedCurrent.EqualAsync(result,
                Result<object>.Fail(Errors.Problem(ErrorCode.Conflict, AtomicTriadStagedProtocol.DuplicateDetail)));
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(AtomicTriadOutboxImage.Domain(state.Store, state.Partition))
            .IsEquivalentTo(before, TUnit.Assertions.Enums.CollectionOrdering.Matching), failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedCurrent.EmptyAsync(state), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task DeniedAsync(AtomicTriadStagedFixture state)
    {
        var operation = state.Operation(AtomicTriadStagedRecipe.Command(state.Partition, false));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var result = state.Owner.ApplyEmbedded(operation, TestContext.Current!.Execution.CancellationToken);
            ServerFailureObserver.Observe(() => AtomicTriadStagedReplay.Retain(state, operation, result), failures);
            await AtomicTriadStagedCurrent.EqualAsync(result,
                Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, AtomicTriadStagedProtocol.ScopeDetail)));
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedCurrent.EmptyAsync(state), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task HealthyAsync(AtomicTriadStagedFixture state)
    {
        state.Healthy = AtomicTriadStagedRecipe.Command(state.Partition, false);
        var operation = state.Operation(state.Healthy);
        var position = state.Store.Position + AtomicTriadStagedProtocol.InitialRevision;
        var committed = state.Owner.ApplyEmbedded(operation, TestContext.Current!.Execution.CancellationToken);
        state.HealthyCommitted = Errors.Code(committed.Problem) is null;
        state.Receipt = new(state.Healthy.CommandId, new(state.Store.Identity.Incarnation, state.Partition.AtomicPartitionId,
            position), AtomicTriadStagedRecipe.Receipts, DurabilityProfile.ProcessDurable, 1);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => AtomicTriadStagedReplay.Retain(state, operation, committed), failures);
        ServerFailureObserver.Observe(() => state.CommittedAt = QueueWholeFlowStorage.Clock(state.Store), failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedOutbox.ReceiptAsync(committed.Get<CommitReceipt>(), state.Receipt), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
