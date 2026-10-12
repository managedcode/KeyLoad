using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class CanonicalOutcomeReadBudgetAssertions
{
    private const string SeedId = "inspection-outcome-seed";
    private const string SeedJson = "{\"seed\":true}";
    private const long SeedRevision = 1;
    private const string PutDocumentKind = "putDocument";

    internal static async Task UnchangedAsync(IAtomicStore store, string[] expected, long position)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await ImageAsync(store, expected);
    }

    internal static async Task ImageAsync(IAtomicStore store, string[] expected)
    {
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(store).SequenceEqual(expected)).IsTrue();
    }

    internal static async Task DocumentAsync(DatabaseEngine database, C1OutcomeInspectionFixture fixture)
    {
        var document = database.GetDocument(C1OutcomeInspectionAssertions.AdminId,
            new(fixture.Partition, C1OutcomeInspectionFixture.ResourceName, SeedId));
        await Assert.That(document).IsNotNull();
        await Assert.That(document?.Json).IsEqualTo(SeedJson);
        await Assert.That(document?.Revision).IsEqualTo((long?)SeedRevision);
    }

    internal static async Task OriginalAsync(CommandOutcomeSelection expected, CommandOutcomeSelection actual,
        C1OutcomeInspectionFixture fixture)
    {
        await Assert.That(actual.Key.SequenceEqual(expected.Key)).IsTrue();
        await Assert.That(actual.Outcome).IsNotNull();
        var outcome = actual.Outcome ?? throw new InvalidOperationException(C1OutcomeInspectionFixture.SuccessMessage);
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(outcome.Partition).IsEqualTo(fixture.Partition);
        await Assert.That(outcome.Incarnation).IsEqualTo(fixture.Incarnation);
        await Assert.That(outcome.Result.Error).IsNull();
        await Assert.That(NativeSerialization.Serialize(outcome).SequenceEqual(NativeSerialization.Serialize(expected.Outcome))).IsTrue();
        var receipt = outcome.Result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(fixture.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        var mutation = receipt.Mutations.Single();
        await Assert.That(mutation.Kind).IsEqualTo(PutDocumentKind);
        await Assert.That(mutation.Resource).IsEqualTo(C1OutcomeInspectionFixture.ResourceName);
        await Assert.That(mutation.Id).IsEqualTo(SeedId);
        await Assert.That(mutation.Revision).IsEqualTo(SeedRevision);
        await Assert.That(mutation.CompositionReferences.Length).IsEqualTo(0);
    }
}
