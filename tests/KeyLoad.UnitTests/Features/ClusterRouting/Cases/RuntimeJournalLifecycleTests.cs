using KeyLoad;
using KeyLoad.Core;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RuntimeJournalLifecycleTests
{
    private const string JournalName = "native-journal";
    private const string FirstInstance = "11223344-5566-7788-99aa-bbccddeeff00";
    private const string SecondInstance = "22334455-6677-8899-aabb-ccddeeff0011";
    private const int MultiPageLength = 80_000;

    [Test]
    public async Task RealStoreCreatesAppendsReplacesReadsAndDeletesOpaqueContent()
    {
        using var fixture = new RuntimeJournalFixture();
        var created = fixture.Create(JournalName, FirstInstance, new(StringComparer.Ordinal) { ["origin"] = "test" });
        var content = Enumerable.Range(0, MultiPageLength).Select(index => (byte)(index % byte.MaxValue)).ToArray();
        var appended = fixture.Submit(RuntimeJournalFixture.Mutation(RuntimeJournalAction.Append, created, content)).Snapshot!;
        await Assert.That(appended.ContentRevision).IsEqualTo(1L);
        await Assert.That(appended.Length).IsEqualTo((long)MultiPageLength);

        var first = fixture.Engine.ReadRuntimeJournal(RuntimeJournalFixture.JournalPrincipal,
            new(JournalName, appended.InstanceId, appended.OwnerGeneration, appended.ContentRevision, 0));
        var second = fixture.Engine.ReadRuntimeJournal(RuntimeJournalFixture.JournalPrincipal,
            new(JournalName, appended.InstanceId, appended.OwnerGeneration, appended.ContentRevision, first.Data.Length));
        await Assert.That(first.Data.Length).IsEqualTo(65_536);
        await Assert.That(first.IsCompleted).IsFalse();
        await Assert.That(second.Data.Length).IsEqualTo(MultiPageLength - first.Data.Length);
        await Assert.That(second.IsCompleted).IsTrue();
        await Assert.That(first.Data.ToArray().Concat(second.Data.ToArray()))
            .IsEquivalentTo(content, CollectionOrdering.Matching);

        var replacement = new byte[] { 7, 4, 2, 9 };
        var replaced = fixture.Submit(RuntimeJournalFixture.Mutation(RuntimeJournalAction.Replace, appended, replacement)).Snapshot!;
        var page = fixture.Engine.ReadRuntimeJournal(RuntimeJournalFixture.JournalPrincipal,
            new(JournalName, replaced.InstanceId, replaced.OwnerGeneration, replaced.ContentRevision, 0));
        await Assert.That(page.Data.ToArray()).IsEquivalentTo(replacement);
        await Assert.That(replaced.ContentRevision).IsEqualTo(2L);

        var deleted = fixture.Submit(RuntimeJournalFixture.Mutation(RuntimeJournalAction.Delete, replaced));
        await Assert.That(deleted.Applied).IsTrue();
        await Assert.That(fixture.Engine.GetRuntimeJournalHeader(RuntimeJournalFixture.JournalPrincipal, JournalName)).IsNull();
        await Assert.That(fixture.Engine.ReadRuntimeJournalCatalog(RuntimeJournalFixture.JournalPrincipal).Journals).IsEmpty();
        var recreated = fixture.Create(JournalName, SecondInstance);
        await Assert.That(recreated.InstanceId).IsNotEqualTo(replaced.InstanceId);
        var stale = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() => fixture.Submit(
            RuntimeJournalFixture.Mutation(RuntimeJournalAction.Append, replaced, [1]))));
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.Conflict);
    }

    [Test]
    public async Task MetadataCasFencesOwnerAndPoisonButNotSameOwnerBookkeeping()
    {
        using var fixture = new RuntimeJournalFixture();
        var created = fixture.Create(JournalName, FirstInstance);
        var claimed = fixture.Submit(RuntimeJournalFixture.Mutation(RuntimeJournalAction.UpdateMetadata, created,
            etag: created.MetadataETag, set: new(StringComparer.Ordinal) { ["DurableJobsOwner"] = "silo-a" })).Snapshot!;
        await Assert.That(claimed.OwnerGeneration).IsEqualTo(created.OwnerGeneration + 1);
        var staleEtag = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() => fixture.Submit(
            RuntimeJournalFixture.Mutation(RuntimeJournalAction.UpdateMetadata, created, etag: created.MetadataETag,
                set: new(StringComparer.Ordinal) { ["other"] = "stale" }))));
        await Assert.That(staleEtag.Code).IsEqualTo(ErrorCode.Conflict);

        var sameOwner = fixture.Submit(RuntimeJournalFixture.Mutation(RuntimeJournalAction.UpdateMetadata, claimed,
            etag: claimed.MetadataETag, set: new(StringComparer.Ordinal)
            {
                ["DurableJobsOwner"] = "silo-a",
                ["DurableJobsClosed"] = "true",
                ["DurableJobsMembershipVersion"] = "2",
                ["DurableJobsAdoptedCount"] = "3"
            })).Snapshot!;
        await Assert.That(sameOwner.OwnerGeneration).IsEqualTo(claimed.OwnerGeneration);
        await Assert.That(sameOwner.Properties["DurableJobsClosed"]).IsEqualTo("true");
        await Assert.That(sameOwner.Properties["DurableJobsMembershipVersion"]).IsEqualTo("2");
        await Assert.That(sameOwner.Properties["DurableJobsAdoptedCount"]).IsEqualTo("3");

        var poisoned = fixture.Submit(RuntimeJournalFixture.Mutation(RuntimeJournalAction.UpdateMetadata, sameOwner,
            etag: sameOwner.MetadataETag, set: new(StringComparer.Ordinal) { ["DurableJobsPoisoned"] = "true" })).Snapshot!;
        await Assert.That(poisoned.OwnerGeneration).IsEqualTo(sameOwner.OwnerGeneration + 1);
        var unpoisoned = fixture.Submit(RuntimeJournalFixture.Mutation(RuntimeJournalAction.UpdateMetadata, poisoned,
            etag: poisoned.MetadataETag, remove: ["DurableJobsPoisoned"])).Snapshot!;
        await Assert.That(unpoisoned.OwnerGeneration).IsEqualTo(poisoned.OwnerGeneration + 1);
        await Assert.That(unpoisoned.Properties["DurableJobsOwner"]).IsEqualTo("silo-a");
        var stale = RuntimeJournalFixture.Mutation(RuntimeJournalAction.Append, claimed, [1]);
        await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() => fixture.Submit(stale)));
        await Assert.That(fixture.Engine.GetRuntimeJournalHeader(RuntimeJournalFixture.JournalPrincipal, JournalName)?.OwnerGeneration)
            .IsEqualTo(unpoisoned.OwnerGeneration);
    }
}
