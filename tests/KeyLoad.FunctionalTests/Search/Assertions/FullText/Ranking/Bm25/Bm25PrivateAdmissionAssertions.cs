using KeyLoad.Core.Search;
using KeyLoad.FunctionalTests.ResourceExecution;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal static class Bm25PrivateAdmissionAssertions
{
    internal static async Task RequirePublicationAsync(TestDatabase database,
        Bm25CorpusPublicationPhaseCommand phase, Result<object> result, PrincipalRecord subject, long totalLength,
        (CommitToken Token, long Applied) expected)
    {
        await Assert.That(Errors.Code(result.Problem).Value).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(phase.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(database.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(database.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token).IsEqualTo(expected.Token);
        await Assert.That(database.ReadStoredOutcome(phase.CommandId).CanonicalSequence).IsEqualTo(0L);
        await Assert.That(database.Database.LastApplied).IsEqualTo(expected.Applied);
        await Assert.That(database.Store.Position).IsEqualTo(expected.Token.Position);
        var expectedReceipt = new CommitReceipt(phase.CommandId, expected.Token, [],
            database.Database.Durability, database.Database.AcknowledgedPersistedCopies);
        await ReplayResultAssertions.BytesAsync(OrleansSerialization.Serialize(expectedReceipt), OrleansSerialization.Serialize(receipt));
        await Assert.That(receipt.Durability).IsEqualTo(database.Database.Durability);
        await Assert.That(receipt.Mutations).IsEmpty();
        await Assert.That(phase.Current.Generation).IsEqualTo(phase.CommandId);
        await Assert.That(phase.Current.PrincipalId).IsEqualTo(subject.Id);
        await Assert.That(phase.Current.PolicyEpoch).IsEqualTo(subject.PolicyEpoch);
        await Assert.That(phase.Current.Statistics.DocumentCount).IsEqualTo(1);
        await Assert.That(phase.Current.Statistics.TotalLength).IsEqualTo(totalLength);
        await Assert.That(phase.Current.Statistics.Terms).IsEquivalentTo(["needle"], CollectionOrdering.Matching);
        await Assert.That(phase.Current.Statistics.DocumentFrequencies).IsEquivalentTo([1], CollectionOrdering.Matching);
        var stored = database.Store.Read(view => view.ReadOwnedValue(Bm25CorpusKeys.Current(phase.Current)))
            ?? throw new InvalidOperationException("The actual private corpus publication is missing.");
        await ReplayResultAssertions.BytesAsync(OrleansSerialization.Serialize(phase.Current), stored);
        await ReplayResultAssertions.SameAsync(result, database.Database.ResolveSubmissionOutcome(
            database.ReadStoredOutcome(phase.CommandId).PreparedCommand!));
    }
}
