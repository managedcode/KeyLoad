using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaAppliedOutcomeAuthorityFault
{
    internal static async Task RunAsync(ReplicaSubmissionOutcomeFlow owner, ReplicatedOperation operation, OperationResult expected)
    {
        var scope = CommandOutcomePartitionIdentity.Resolve(operation);
        var selection = owner.Database.Store.Read(view => CommandOutcomeKeyResolver.Select(view,
            operation.PrincipalId, operation.Id, scope));
        var outcome = selection.Outcome ?? throw new InvalidOperationException("The genuine applied outcome is required.");
        var partition = scope.Partition ?? throw new InvalidOperationException("The genuine blob partition is required.");
        var locator = CommandOutcomePartitionLocatorSerialization.ScopedKey(partition, operation.PrincipalId, operation.Id);
        var original = owner.Outcome(operation);
        var originalLocator = owner.Database.Store.Read(view => view.ReadOwnedValue(locator))
            ?? throw new InvalidOperationException("The original native outcome locator is required.");
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            owner.Database.Store.Commit((tx, _) => { tx.Delete(selection.Key); tx.Delete(locator); return true; });
            await RefusedAsync(owner, operation, ErrorCode.RecoveryRequired);
            Restore(owner, selection.Key, original, locator, originalLocator);
            await ReplicaSubmissionOutcomeFlow.RequireResultAsync(expected,
                owner.Database.Database.ResolveAppliedSubmissionOutcome(operation));
            (StoredOutcome Row, ErrorCode Code)[] faults =
            [
                (outcome with { Partition = partition with { PartitionKey = "foreign-outcome-partition" } }, ErrorCode.Corruption),
                (outcome with { Incarnation = Guid.NewGuid() }, ErrorCode.TokenInvalidated),
                (outcome with { Fingerprint = "not-the-original-frozen-command" }, ErrorCode.Conflict)
            ];
            foreach (var fault in faults)
            {
                owner.Database.Store.Commit((tx, _) => { tx.Put(selection.Key, NativeSerialization.Serialize(fault.Row)); return true; });
                await RefusedAsync(owner, operation, fault.Code);
            }
        }, failures);
        ServerFailureObserver.Observe(() => Restore(owner, selection.Key, original, locator, originalLocator), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(original, owner.Outcome(operation));
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(expected,
            owner.Database.Database.ResolveAppliedSubmissionOutcome(operation));
    }

    private static async Task RefusedAsync(ReplicaSubmissionOutcomeFlow owner, ReplicatedOperation operation, ErrorCode expected)
    {
        var state = ReplicaSubmissionOutcomeState.Read(owner.Database);
        var actual = Assert.ThrowsExactly<KeyLoadException>(() => owner.Database.Database.ResolveAppliedSubmissionOutcome(operation));
        await Assert.That(actual.Code).IsEqualTo(expected);
        await state.RequireAsync(ReplicaSubmissionOutcomeState.Read(owner.Database));
    }

    private static void Restore(ReplicaSubmissionOutcomeFlow owner, byte[] key, byte[] bytes, byte[] locator, byte[] locatorBytes)
        => owner.Database.Store.Commit((tx, _) => { tx.Put(key, bytes); tx.Put(locator, locatorBytes); return true; });
}
