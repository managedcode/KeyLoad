using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementRetainedOutcomeAssertions
{
    private const int AuthorityVersion = 1;
    private const int ExpectedOutcomes = 8;
    private const long InitialPolicyEpoch = 1;

    internal static async Task RequireAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementRetainedOutcome[] records)
    {
        await Assert.That(records.Length).IsEqualTo(ExpectedOutcomes);
        var beganAt = records.Single(record => record.Index == ControlledPartitionMovementOutcomeInventory.BeginIndex).Operation.EvaluatedAt;
        foreach (var record in records)
        { await RequireRecordAsync(source, target, record, beganAt); }
    }

    private static async Task RequireRecordAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementRetainedOutcome record, DateTimeOffset beganAt)
    {
        var bytes = source.Store.Read(view => view.ReadOwnedValue(record.Key));
        await Assert.That(bytes is not null && bytes.AsSpan().SequenceEqual(record.Bytes)).IsTrue();
        await Assert.That(target.Store.Read(view => view.ReadOwnedValue(record.Key))).IsNull();
        await NoTargetCopyAsync(source, target, record);
        var outcome = NativeSerialization.Deserialize<StoredOutcome>(bytes!);
        await Assert.That(outcome.Incarnation).IsEqualTo(source.PhysicalOwner.Incarnation);
        await Assert.That(outcome.PolicyEpoch).IsEqualTo(InitialPolicyEpoch);
        await Assert.That(outcome.ScopeKind).IsEqualTo(record.Scope);
        if (record.Scope == CommandOutcomeScopeKind.Partition)
        { await ControlledPartitionMovementOutcomeValueAssertions.EqualAsync(outcome.Partition, ControlledPartitionMovementCorpus.Partition); }
        else
        { await Assert.That(outcome.Partition).IsNull(); }
        if (record.LocatorKey is { } locator)
        {
            await Assert.That(source.Store.Read(view => view.ReadOwnedValue(locator))!.SequenceEqual(record.Key)).IsTrue();
            await Assert.That(target.Store.Read(view => view.ReadOwnedValue(locator))).IsNull();
        }
        var blob = record.Index >= ControlledPartitionMovementOutcomeInventory.BeginIndex;
        if (blob)
        {
            await ControlledPartitionMovementOutcomeValueAssertions.EqualAsync(outcome.BlobAuthority,
                new BlobOutcomeAuthority(AuthorityVersion, ControlledPartitionMovementOutcomeInventory.BeginId,
                    PhysicalShardCatalogFixture.RootPrincipalId));
        }
        else
        { await Assert.That(outcome.BlobAuthority).IsNull(); }
        await Assert.That(outcome.CompositionAuthority).IsNull();
        await Assert.That(outcome.OnlineTextAuthority).IsNull();
        await Assert.That(outcome.RemoteTransferAuthority).IsNull();
        await Assert.That(outcome.RemoteTransferFailureAuthority).IsNull();
        await ControlledPartitionMovementOutcomeValueAssertions.RequireAsync(source, record, beganAt);
    }

    private static async Task NoTargetCopyAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementRetainedOutcome record)
    {
        var principal = record.Operation.PrincipalId;
        var command = record.Operation.Id;
        var scopedLocator = KeySpace.OutcomeLocatorV2(ControlledPartitionMovementCorpus.Partition, principal, command);
        await Assert.That(target.Store.Read(view => view.ReadOwnedValue(scopedLocator))).IsNull();
        if (record.Scope == CommandOutcomeScopeKind.Global)
        { await Assert.That(source.Store.Read(view => view.ReadOwnedValue(scopedLocator))).IsNull(); }
        await Assert.That(target.Store.Read(view => view.ReadOwnedValue(KeySpace.GlobalOutcome(principal, command)))).IsNull();
        await Assert.That(target.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(ControlledPartitionMovementCorpus.Partition, principal, command)))).IsNull();
        await Assert.That(target.Store.Read(view => view.ReadOwnedValue(KeySpace.UnknownOutcome(principal, command)))).IsNull();
        await Assert.That(source.Store.Read(view => view.ReadOwnedValue(KeySpace.UnknownOutcome(principal, command)))).IsNull();
        var wrongScope = record.Scope == CommandOutcomeScopeKind.Global
            ? KeySpace.PartitionOutcome(ControlledPartitionMovementCorpus.Partition, principal, command)
            : KeySpace.GlobalOutcome(principal, command);
        await Assert.That(source.Store.Read(view => view.ReadOwnedValue(wrongScope))).IsNull();
    }
}
