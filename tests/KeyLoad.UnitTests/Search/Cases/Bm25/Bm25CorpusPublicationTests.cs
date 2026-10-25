using KeyLoad.UnitTests.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Search;

internal sealed class Bm25CorpusPublicationTests
{
    [Test]
    public Task CurrentHeadRequiresExactTermsAndOutboxTailWhileOriginalReceiptAndHealthyScorerRemainStable()
        => Bm25CorpusPublicationFlow.RunAsync(async flow =>
        {
            var original = flow.Capture(Guid.NewGuid(), null);
            var receipt = flow.Publish(original);
            await Assert.That(flow.Current()!.DocumentCount).IsEqualTo(1);
            await Assert.That(flow.Current("different")).IsNull();
            flow.Database.Commit(new PutDocument(Bm25CorpusPublicationFlow.Collection, "second", "{\"text\":\"needle\"}"));
            await Assert.That(flow.Current()).IsNull();
            var before = CanonicalStoreCut.Read(flow.Database);
            var replay = flow.Publish(original);
            await ReplayResultAssertions.BytesAsync(OrleansSerialization.Serialize(receipt), OrleansSerialization.Serialize(replay));
            await before.RequireAsync(CanonicalStoreCut.Read(flow.Database));
            var next = flow.Capture(Guid.NewGuid(), original.CommandId);
            flow.Publish(next);
            await Assert.That(flow.Current()!.DocumentCount).IsEqualTo(2);
            await Assert.That(flow.Current()!.TotalLength).IsEqualTo(2L);
            var rows = await flow.SearchAsync();
            await Assert.That(rows.Select(row => row.Document.Reference.Id).ToArray())
                .IsEquivalentTo(["first", "second"], CollectionOrdering.Matching);
        });

    [Test]
    public Task RevocationAndHigherEpochRepairNeverReviveOriginalSubjectScopedReceiptThenFreshPublicationSucceeds()
        => Bm25CorpusPublicationFlow.RunAsync(async flow =>
        {
            var original = flow.Capture(Guid.NewGuid(), null);
            flow.Publish(original);
            var phase = Bm25CorpusPublicationFlow.Phase(original);
            var revoked = flow.SetPrincipal(flow.Principal with
            {
                PolicyEpoch = 2,
                Grants = [new(flow.Database.Partition.DatabaseId,
                Bm25CorpusPublicationFlow.Collection, Capability.DocumentsRead)]
            });
            await Assert.That(revoked.PolicyEpoch).IsEqualTo(2L);
            var before = CanonicalStoreCut.Read(flow.Database);
            var denied = Assert.ThrowsExactly<KeyLoadException>(() => flow.Database.Database.CreateVerifiedBm25CorpusPublication(
                Bm25CorpusPublicationFlow.Root, phase, flow.Database.CreateReadWork(Bm25CorpusPublicationFlow.Token)));
            await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(Errors.Code(flow.Database.SubmitIssued(original).Problem)).IsEqualTo(ErrorCode.PermissionDenied);
            var searchDenied = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => flow.SearchAsync());
            await Assert.That(searchDenied!.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await before.RequireAsync(CanonicalStoreCut.Read(flow.Database));
            var restored = flow.SetPrincipal(flow.Principal with
            {
                PolicyEpoch = 3
            });
            await Assert.That(restored.PolicyEpoch).IsEqualTo(3L);
            var repairedBefore = CanonicalStoreCut.Read(flow.Database);
            var old = Assert.ThrowsExactly<KeyLoadException>(() => flow.Database.Database.CreateVerifiedBm25CorpusPublication(
                Bm25CorpusPublicationFlow.Root, phase, flow.Database.CreateReadWork(Bm25CorpusPublicationFlow.Token)));
            await Assert.That(old.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(Errors.Code(flow.Database.SubmitIssued(original).Problem)).IsEqualTo(ErrorCode.PermissionDenied);
            await repairedBefore.RequireAsync(CanonicalStoreCut.Read(flow.Database));
            var healthy = flow.Capture(Guid.NewGuid(), original.CommandId);
            flow.Publish(healthy);
            await Assert.That(Bm25CorpusPublicationFlow.Phase(healthy).Current.PolicyEpoch).IsEqualTo(3L);
            await Assert.That(flow.Current()!.DocumentCount).IsEqualTo(1);
            await Assert.That((await flow.SearchAsync()).Single().Document.Reference.Id).IsEqualTo("first");
        });

}
