using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Owns deliberate actual current-format scoped metadata corruption and exact restoration.</summary>
internal static class RetiredOutcomeMetadataFaultScope
{
    private const string ForeignLocatorPrincipal = "movement-locator-corrupt";

    internal static async Task WithFaultAsync(ControlledPartitionMovementNode source, ReplicatedOperation original,
        string subject, bool corruptOutcome, bool corruptLocator, Func<Task> operation)
    {
        var partition = ControlledPartitionMovementCorpus.Partition;
        var outcomeKey = KeySpace.PartitionOutcome(partition, subject, original.Id);
        var locatorKey = KeySpace.OutcomeLocatorV2(partition, subject, original.Id);
        var originalKey = KeySpace.PartitionOutcome(partition, original.PrincipalId, original.Id);
        var retained = source.Store.Read(view => view.ReadOwnedValue(originalKey))
            ?? throw new InvalidOperationException("The actual original outcome is absent.");
        var outcomeBefore = source.Store.Read(view => view.ReadOwnedValue(outcomeKey));
        var locatorBefore = source.Store.Read(view => view.ReadOwnedValue(locatorKey));
        var changed = NativeSerialization.Deserialize<StoredOutcome>(retained) with
        { ScopeKind = CommandOutcomeScopeKind.Global, Partition = null };
        source.Store.Commit((transaction, _) =>
        {
            transaction.Put(outcomeKey, corruptOutcome ? NativeSerialization.Serialize(changed) : retained);
            transaction.Put(locatorKey, corruptLocator
                ? KeySpace.PartitionOutcome(partition, ForeignLocatorPrincipal, original.Id) : originalKey);
            return true;
        });
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        ServerFailureObserver.Observe(() => source.Store.Commit((transaction, _) =>
        {
            if (outcomeBefore is null)
            { transaction.Delete(outcomeKey); }
            else
            { transaction.Put(outcomeKey, outcomeBefore); }
            if (locatorBefore is null)
            { transaction.Delete(locatorKey); }
            else
            { transaction.Put(locatorKey, locatorBefore); }
            return true;
        }), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
