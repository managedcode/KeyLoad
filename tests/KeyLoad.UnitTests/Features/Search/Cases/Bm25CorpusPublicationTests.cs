using KeyLoad.Core.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

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
            var before = ReplicaSubmissionOutcomeState.Read(flow.Database);
            var replay = flow.Publish(original);
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(receipt), NativeSerialization.Serialize(replay));
            await before.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
            var next = flow.Capture(Guid.NewGuid(), original.Id);
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
            var before = ReplicaSubmissionOutcomeState.Read(flow.Database);
            var denied = Assert.ThrowsExactly<KeyLoadException>(() => flow.Database.Database.CreateVerifiedBm25CorpusPublication(
                Bm25CorpusPublicationFlow.Root, phase, flow.Database.CreateReadWork(Bm25CorpusPublicationFlow.Token)));
            await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(flow.Database.SubmitIssued(original).Error).IsEqualTo(ErrorCode.PermissionDenied);
            var searchDenied = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => flow.SearchAsync());
            await Assert.That(searchDenied!.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await before.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
            var restored = flow.SetPrincipal(flow.Principal with { PolicyEpoch = 3 });
            await Assert.That(restored.PolicyEpoch).IsEqualTo(3L);
            var repairedBefore = ReplicaSubmissionOutcomeState.Read(flow.Database);
            var old = Assert.ThrowsExactly<KeyLoadException>(() => flow.Database.Database.CreateVerifiedBm25CorpusPublication(
                Bm25CorpusPublicationFlow.Root, phase, flow.Database.CreateReadWork(Bm25CorpusPublicationFlow.Token)));
            await Assert.That(old.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(flow.Database.SubmitIssued(original).Error).IsEqualTo(ErrorCode.PermissionDenied);
            await repairedBefore.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
            var healthy = flow.Capture(Guid.NewGuid(), original.Id);
            flow.Publish(healthy);
            await Assert.That(Bm25CorpusPublicationFlow.Phase(healthy).Current.PolicyEpoch).IsEqualTo(3L);
            await Assert.That(flow.Current()!.DocumentCount).IsEqualTo(1);
            await Assert.That((await flow.SearchAsync()).Single().Document.Reference.Id).IsEqualTo("first");
        });

    [Test]
    public Task RetainedRawAdmissionHonorsExactByteAndRecordBoundariesAndExcludesLargerReplacedHead()
        => Bm25CorpusPublicationFlow.RunAsync(async flow =>
        {
            var original = flow.Capture(Guid.NewGuid(), null, "needle another");
            flow.Publish(original);
            var replacement = Bm25CorpusPublicationFlow.Phase(flow.Capture(Guid.NewGuid(), original.Id)).Current;
            var prior = Bm25CorpusPublicationFlow.Phase(original).Current;
            var key = Bm25CorpusKeys.Current(replacement);
            var replacementBytes = NativeSerialization.Serialize(replacement);
            var priorBytes = flow.Database.Store.Read(view => view.ReadOwnedValue(Bm25CorpusKeys.Current(prior)))
                ?? throw new InvalidOperationException("The actual original retained head is missing.");
            var exact = checked(key.Length + replacementBytes.Length);
            await Assert.That(replacementBytes.Length).IsLessThan(priorBytes.Length);
            var full = flow.Database.Database.Limits with { MaxBatchBytes = exact };
            var before = ReplicaSubmissionOutcomeState.Read(flow.Database);
            flow.RequireAdmission(replacement, full);
            var deniedBytes = Assert.ThrowsExactly<KeyLoadException>(() => flow.RequireAdmission(replacement,
                full with { MaxBatchBytes = exact - 1 }));
            await Assert.That(deniedBytes.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await before.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
            flow.SetPrincipal(flow.Principal with { Id = "bm25-second-subject" });
            var other = Bm25CorpusPublicationFlow.Phase(flow.Capture(Guid.NewGuid(), null, subject: "bm25-second-subject")).Current;
            var count = flow.Database.Store.Read(view =>
            {
                var page = view.Scan(Bm25CorpusKeys.Prefix(flow.Database.Partition), flow.Database.Database.Limits.MaxScanRecords, null);
                if (page.HasMore)
                { throw new InvalidOperationException("The actual retained family was not complete."); }
                return page.Records.Length;
            });
            var countBefore = ReplicaSubmissionOutcomeState.Read(flow.Database);
            var deniedRecords = Assert.ThrowsExactly<KeyLoadException>(() => flow.RequireAdmission(other,
                flow.Database.Database.Limits with { MaxScanRecords = count }));
            await Assert.That(deniedRecords.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await countBefore.RequireAsync(ReplicaSubmissionOutcomeState.Read(flow.Database));
        });
}
