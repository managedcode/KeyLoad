using KeyLoad.Core.CommandOutcomes.Contracts;
using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using static KeyLoad.ProcessRecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.FunctionalTests.EventStreams;

internal static class SharedDomainHistoryOperations
{
    private const string EpochDenied = "The principal policy changed since this command was evaluated.";
    private const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    private const int FingerprintCharacters = 64;
    private const string StaleLeaseDetail = "The delivery lease is stale.";

    internal static async Task SaveAsync(IAtomicStore store, SharedDomainState state, PartitionRef partition,
        Command<DatabaseCommand> operation, Result<object> result)
    {
        var key = KeySpace.PartitionOutcome(partition, Writer, operation.CommandId);
        var locator = KeySpace.OutcomeLocator(partition, Writer, operation.CommandId);
        var bytes = SharedDomainImage.Required(store, key);
        var pointer = SharedDomainImage.Required(store, locator);
        await Assert.That(pointer.AsSpan().SequenceEqual(key)).IsTrue();
        var outcome = OrleansSerialization.Deserialize<StoredOutcome>(bytes);
        await Assert.That(outcome.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(outcome.PolicyEpoch).IsEqualTo(state.PolicyEpoch);
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(outcome.Partition).IsEqualTo(partition);
        await Assert.That(outcome.Fingerprint.Length).IsEqualTo(FingerprintCharacters);
        await Assert.That(outcome.Fingerprint.All(char.IsAsciiHexDigit)).IsTrue();
        await Assert.That(outcome.OnlineTextAuthority).IsNull();
        await Assert.That(outcome.RemoteTransferFailureAuthority).IsNull();
        if (result.Value is ReceiveResult)
        {
            await ReplayResultAssertions.Same<ReceiveResult>(outcome.Result, result);
        }
        else if (Errors.Code(result.Problem).Value is null)
        {
            await ReplayResultAssertions.Same<CommitReceipt>(outcome.Result, result);
        }
        else
        {
            await SharedDomainValueAssertions.EqualAsync(outcome.Result, result);
        }
        state.History.Add(new(partition, operation, result, key, bytes, locator, pointer));
    }

    internal static async Task AllAsync(DatabaseEngine database, IAtomicStore store, SharedDomainState state, CancellationToken token)
    {
        foreach (var item in state.History)
        {
            token.ThrowIfCancellationRequested();
            var before = SharedDomainImage.Capture(store);
            var position = store.Position;
            await ReleaseAsync(database, state, item, token);
            await SharedDomainImage.BytesAsync(store, item.OutcomeKey, item.OutcomeBytes);
            await SharedDomainImage.BytesAsync(store, item.LocatorKey, item.LocatorBytes);
            await SharedDomainImage.SameAsync(store, before, position);
        }
    }

    private static async Task ReleaseAsync(DatabaseEngine database, SharedDomainState state, SharedDomainHistory item, CancellationToken token)
    {
        var outcome = OrleansSerialization.Deserialize<StoredOutcome>(item.OutcomeBytes);
        if (state.Denied)
        {
            await SharedDomainValueAssertions.FailureAsync(database.ResolveOutcome(item.Operation), ErrorCode.PermissionDenied, ScopeDenied);
            return;
        }
        if (Errors.Code(item.Result.Problem).Value is not null)
        {
            await SameResultAsync(SharedDomainOperations.Apply(database, item.Operation, token), item.Result);
            await SameResultAsync(database.ResolveOutcome(item.Operation), item.Result);
            return;
        }
        if (outcome.PolicyEpoch != state.PolicyEpoch)
        {
            await SharedDomainValueAssertions.FailureAsync(database.ResolveOutcome(item.Operation), ErrorCode.PermissionDenied, EpochDenied);
            return;
        }
        if (item.Result.Value is ReceiveResult)
        {
            await SharedDomainValueAssertions.FailureAsync(database.ResolveOutcome(item.Operation), ErrorCode.StaleLease, StaleLeaseDetail);
            return;
        }
        await SameResultAsync(SharedDomainOperations.Apply(database, item.Operation, token), item.Result);
        await SameResultAsync(database.ResolveOutcome(item.Operation), item.Result);
    }

    private static async Task SameResultAsync(Result<object> actual, Result<object> expected)
    {
        if (Errors.Code(expected.Problem).Value is null)
        {
            await ReplayResultAssertions.Same<CommitReceipt>(actual, expected);
        }
        else
        {
            await SharedDomainValueAssertions.EqualAsync(actual, expected);
        }
    }
}
