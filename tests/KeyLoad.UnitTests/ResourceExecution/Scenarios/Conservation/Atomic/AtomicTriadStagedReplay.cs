using KeyLoad.Core;
using KeyLoad.Server;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class AtomicTriadStagedReplay
{
    internal static void Retain(AtomicTriadStagedFixture state, Command<DatabaseCommand> operation, Result<object> result)
    {
        state.Produced.Add((operation, result));
        var bytes = state.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(state.Partition,
            operation.UserId!, operation.CommandId))) ?? throw new InvalidOperationException(AtomicTriadStagedProtocol.Missing);
        state.Originals.Add((operation, result, bytes));
    }

    internal static async Task OriginalBytesAsync(AtomicTriadStagedFixture state, Command<DatabaseCommand> operation, byte[] bytes)
        => await Assert.That(state.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(state.Partition,
            operation.UserId!, operation.CommandId)))).IsEquivalentTo(bytes, TUnit.Assertions.Enums.CollectionOrdering.Matching);

    internal static async Task CancelAsync(AtomicTriadStagedFixture state)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var operation = state.Operation(state.Failed);
        await AtomicTriadStagedCurrent.StableAsync(state, async () =>
        {
            OperationCanceledException? observed = null;
            try
            {
                state.Owner.ApplyEmbedded(operation, cancellation.Token);
            }
            catch (OperationCanceledException exception)
            {
                observed = exception;
            }
            await Assert.That(observed).IsNotNull();
            await Assert.That(observed!.CancellationToken).IsEqualTo(cancellation.Token);
            await AtomicTriadStagedCurrent.EmptyAsync(state);
        });
    }

    internal static async Task RunAsync(AtomicTriadStagedFixture state, bool repaired)
    {
        var failures = new List<Exception>();
        foreach (var original in state.Originals)
        {
            await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedCurrent.StableAsync(state, async () =>
            {
                await RequireAsync(state, original.Operation, original.Result, original.Bytes, repaired);
            }), failures);
        }
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(state.Originals.Count).IsEqualTo(state.Produced.Count), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RequireAsync(AtomicTriadStagedFixture state, Command<DatabaseCommand> operation,
        Result<object> original, byte[] bytes, bool repaired)
    {
        var actual = state.Owner.ApplyEmbedded(operation, TestContext.Current!.Execution.CancellationToken);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => OriginalBytesAsync(state, operation, bytes), failures);
        var expected = repaired && Errors.Code(original.Problem) is not null
            ? Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, AtomicTriadStagedProtocol.PolicyDetail)) : original;
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedCurrent.EqualAsync(actual, expected), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (Errors.Code(actual.Problem) is not null)
            {
                await Assert.That(actual.Value).IsNull();
            }
            else
            {
                await AtomicTriadStagedOutbox.ReceiptAsync(actual.Get<CommitReceipt>(), state.Receipt);
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => repaired && state.HealthyCommitted
            ? AtomicTriadStagedCurrent.CompleteAsync(state) : AtomicTriadStagedCurrent.EmptyAsync(state), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
