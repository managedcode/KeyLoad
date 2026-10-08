using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class C1OutcomeInspectionScopeTests
{
    [Test]
    public async Task AcCrs005InspectionUsesExactPartitionWhenCommandIdsRepeat()
    {
        await C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            fixture.CommitBatch(fixture.OtherPartition, fixture.CommandId, "other-partition-seed");
            await C1OutcomeInspectionAssertions.AssertOutcomeAsync(fixture, expected: true);
            await C1OutcomeInspectionAssertions.AssertOutcomeAsync(fixture, expected: true,
                partition: fixture.OtherPartition);
            var absentPartition = fixture.Partition with { PartitionKey = "unseeded-partition" };
            using (var reopenedStore = ZoneTreeExistingStore.Open(new ZoneTreeStoreOptions(fixture.DirectoryPath)
            { Incarnation = fixture.Incarnation }, fixture.Identity.NodeId, UnitExecutionOptions.StorageExecution()))
            {
                await Assert.That(reopenedStore.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(
                    absentPartition, C1OutcomeInspectionAssertions.AdminId, fixture.CommandId)))).IsNull();
                await Assert.That(reopenedStore.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocatorV2(
                    absentPartition, C1OutcomeInspectionAssertions.AdminId, fixture.CommandId)))).IsNull();
            }
            await C1OutcomeInspectionAssertions.AssertOutcomeAsync(fixture, expected: false,
                partition: absentPartition);
            await C1OutcomeInspectionAssertions.AssertOuterOwnerReleasedAsync(fixture);
        }).ConfigureAwait(false);
    }

    [Test]
    public async Task AcCrs005InspectionRejectsOutcomeWithMismatchedCurrentScope()
    {
        await C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            var outcomeKey = KeySpace.PartitionOutcome(fixture.Partition,
                C1OutcomeInspectionAssertions.AdminId, fixture.CommandId);
            fixture.Store.Commit((transaction, _) =>
            {
                var outcome = transaction.GetRecord<StoredOutcome>(outcomeKey)!;
                transaction.PutRecord(outcomeKey, outcome with
                {
                    Partition = fixture.OtherPartition
                });
                return true;
            });
            var outcomeBefore = fixture.Store.Read(view => view.ReadOwnedValue(outcomeKey));
            var locatorKey = KeySpace.OutcomeLocatorV2(fixture.Partition,
                C1OutcomeInspectionAssertions.AdminId, fixture.CommandId);
            var locatorBefore = fixture.Store.Read(view => view.ReadOwnedValue(locatorKey));
            var rejected = await C1OutcomeInspectionAssertions.RunAsync(fixture);
            await C1OutcomeInspectionAssertions.AssertRejectedAsync(rejected, C1OutcomeInspectionFailurePhase.ReadOutcome);
            await C1OutcomeInspectionAssertions.AssertOuterOwnerReleasedAsync(fixture);
            await C1OutcomeInspectionHealthyFollowUp.AssertUnrelatedCurrentOperationAsync(
                fixture, outcomeBefore, locatorBefore);
        }).ConfigureAwait(false);
    }

    [Test]
    public async Task AcCrs005InspectionRejectsNonPartitionScopedOutcome()
    {
        await C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            var outcomeKey = KeySpace.PartitionOutcome(fixture.Partition,
                C1OutcomeInspectionAssertions.AdminId, fixture.CommandId);
            fixture.Store.Commit((transaction, _) =>
            {
                var outcome = transaction.GetRecord<StoredOutcome>(outcomeKey)!;
                transaction.PutRecord(outcomeKey, outcome with
                {
                    ScopeKind = CommandOutcomeScopeKind.Global,
                    Partition = null
                });
                return true;
            });
            var outcomeBefore = fixture.Store.Read(view => view.ReadOwnedValue(outcomeKey));
            var locatorKey = KeySpace.OutcomeLocatorV2(fixture.Partition,
                C1OutcomeInspectionAssertions.AdminId, fixture.CommandId);
            var locatorBefore = fixture.Store.Read(view => view.ReadOwnedValue(locatorKey));
            var rejected = await C1OutcomeInspectionAssertions.RunAsync(fixture);
            await C1OutcomeInspectionAssertions.AssertRejectedAsync(rejected, C1OutcomeInspectionFailurePhase.ReadOutcome);
            await C1OutcomeInspectionAssertions.AssertOuterOwnerReleasedAsync(fixture);
            await C1OutcomeInspectionHealthyFollowUp.AssertUnrelatedCurrentOperationAsync(
                fixture, outcomeBefore, locatorBefore);
        }).ConfigureAwait(false);
    }

    [Test]
    public async Task AcCrs005InspectionRejectsMismatchedAndOrphanCurrentLocators()
    {
        await AssertLocatorRejectedAsync(orphan: false);
        await AssertLocatorRejectedAsync(orphan: true);
    }

    private static async Task AssertLocatorRejectedAsync(bool orphan)
    {
        await C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            var outcomeKey = KeySpace.PartitionOutcome(fixture.Partition,
                C1OutcomeInspectionAssertions.AdminId, fixture.CommandId);
            var locatorKey = KeySpace.OutcomeLocatorV2(fixture.Partition,
                C1OutcomeInspectionAssertions.AdminId, fixture.CommandId);
            fixture.Store.Commit((transaction, _) =>
            {
                if (orphan)
                {
                    transaction.Delete(outcomeKey);
                }
                else
                {
                    transaction.Put(locatorKey, KeySpace.PartitionOutcome(fixture.OtherPartition,
                        C1OutcomeInspectionAssertions.AdminId, fixture.CommandId));
                }
                return true;
            });
            var outcomeBefore = fixture.Store.Read(view => view.ReadOwnedValue(outcomeKey));
            var locatorBefore = fixture.Store.Read(view => view.ReadOwnedValue(locatorKey));
            var rejected = await C1OutcomeInspectionAssertions.RunAsync(fixture);
            await C1OutcomeInspectionAssertions.AssertRejectedAsync(rejected, C1OutcomeInspectionFailurePhase.ReadOutcome);
            await C1OutcomeInspectionAssertions.AssertOuterOwnerReleasedAsync(fixture);
            await C1OutcomeInspectionHealthyFollowUp.AssertUnrelatedCurrentOperationAsync(
                fixture, outcomeBefore, locatorBefore);
        }).ConfigureAwait(false);
    }
}
