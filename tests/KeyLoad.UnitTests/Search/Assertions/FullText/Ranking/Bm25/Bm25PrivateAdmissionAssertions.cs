using KeyLoad.Core;
using ManagedCode.Communication;
using KeyLoad.Core.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25PrivateAdmissionAssertions
{
    internal static async Task RequirePublicationAsync(TestDatabase database,
        Bm25CorpusPublicationPhaseCommand phase, Result<object> result, PrincipalRecord subject, long totalLength)
    {
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(phase.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(database.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(database.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(database.ReadStoredOutcome(phase.CommandId).CanonicalSequence);
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
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(phase.Current), stored);
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(result, database.Database.ResolveSubmissionOutcome(
            database.ReadStoredOutcome(phase.CommandId).PreparedCommand!));
    }
}
