using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class CanonicalOutcomeReadBudgetAssertions
{
    private const string SeedId = "inspection-outcome-seed";
    private const string SeedJson = "{\"seed\":true}";
    private const long SeedRevision = 1;
    private const string PutDocumentKind = "putDocument";
    private const int SigningKeyBytes = 32;

    internal static async Task IdentityAsync(global::KeyLoad.Storage.StoreIdentity expected,
        global::KeyLoad.Storage.StoreIdentity actual)
    {
        await Assert.That(actual.FormatVersion).IsEqualTo(expected.FormatVersion);
        await Assert.That(actual.KeyCodecVersion).IsEqualTo(expected.KeyCodecVersion);
        await Assert.That(actual.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.Durability).IsEqualTo(expected.Durability);
        await Assert.That(actual.DispatchPaused).IsEqualTo(expected.DispatchPaused);
        await Assert.That(actual.ReadGeneration).IsEqualTo(expected.ReadGeneration);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(expected.MinimumReaderContract);
        await Assert.That(expected.SigningKey.Length).IsEqualTo(SigningKeyBytes);
        await Assert.That(actual.SigningKey.Length).IsEqualTo(SigningKeyBytes);
        await Assert.That(actual.SigningKey.Span.SequenceEqual(expected.SigningKey.Span)).IsTrue();
        var expectedDigest = global::System.Security.Cryptography.SHA256.HashData(expected.SigningKey.Span);
        var actualDigest = global::System.Security.Cryptography.SHA256.HashData(actual.SigningKey.Span);
        await Assert.That(Convert.ToHexString(actualDigest)).IsEqualTo(Convert.ToHexString(expectedDigest));
    }

    internal static async Task UnchangedAsync(IAtomicStore store, string[] expected, long position)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await ImageAsync(store, expected);
    }

    internal static async Task ImageAsync(IAtomicStore store, string[] expected)
    {
        await Assert.That(CanonicalOutcomeFixture.Image(store).SequenceEqual(expected)).IsTrue();
    }

    internal static async Task DocumentAsync(DatabaseEngine database, CanonicalOutcomeFixture fixture)
    {
        var document = database.GetDocument(CanonicalOutcomeFixture.AdminId,
            new(fixture.Partition, CanonicalOutcomeFixture.ResourceName, SeedId));
        await Assert.That(document).IsNotNull();
        await Assert.That(document?.Json).IsEqualTo(SeedJson);
        await Assert.That(document?.Revision).IsEqualTo((long?)SeedRevision);
    }

    internal static async Task OriginalAsync(CommandOutcomeSelection expected, CommandOutcomeSelection actual,
        CanonicalOutcomeFixture fixture)
    {
        await Assert.That(actual.Key.SequenceEqual(expected.Key)).IsTrue();
        await Assert.That(actual.Outcome).IsNotNull();
        var outcome = actual.Outcome ?? throw new InvalidOperationException(CanonicalOutcomeFixture.SuccessMessage);
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(outcome.Partition).IsEqualTo(fixture.Partition);
        await Assert.That(outcome.Incarnation).IsEqualTo(fixture.Incarnation);
        await Assert.That(Errors.Code(outcome.Result.Problem)).IsNull();
        await Assert.That(OrleansSerialization.Serialize(outcome).SequenceEqual(OrleansSerialization.Serialize(expected.Outcome))).IsTrue();
        var receipt = outcome.Result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(fixture.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        var mutation = receipt.Mutations.Single();
        await Assert.That(mutation.Kind).IsEqualTo(PutDocumentKind);
        await Assert.That(mutation.Resource).IsEqualTo(CanonicalOutcomeFixture.ResourceName);
        await Assert.That(mutation.Id).IsEqualTo(SeedId);
        await Assert.That(mutation.Revision).IsEqualTo(SeedRevision);
        await Assert.That(mutation.CompositionReferences.Length).IsEqualTo(0);
    }
}
