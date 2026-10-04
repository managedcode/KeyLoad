using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed class DatabaseCompositionCompatibilityTests
{
    // Emitted by NativeSerialization in committed pre-composition 8071148cd83bfecd67332c2a794248bf66c6b4ec.
    // The writer was a private KeyLoad.UnitTests friend assembly built against that commit's Abstractions/Core.
    // These bytes cover one historical value shape, not every older store or downgrade path.
    private const string LegacyReceipt = "IAADMQHtxTDgSWtleWxvYWQuY29udHJhY3QubXV0YXRpb24tcmVjZWlwdC52MehAF1B1dERvY3VtZW50QRNkb2N1bWVudHNBE2xlZ2FjeS00MgEd4OA=";
    private const string LegacyOutcome = "IAADMQFK9V2dO2tleWxvYWQuY29yZS52MS5TdG9yZWRPdXRjb21l6EBHbGVnYWN5LWRvY3VtZW50LWNvbW1hbmQtZmluZ2VycHJpbnRBIaqqqqq7u8zM3d3u7u7u7u4BLSHowAHBAcEBMQHfZKqbRWtleWxvYWQuY29udHJhY3QuY29tbWl0LXJlY2VpcHQudjHoQCERERERIiIzM0REVVVVVVVVIehAIaqqqqq7u8zM3d3u7u7u7u5BM3RlbmFudC9kYXRhYmFzZS9wYXJ0aXRpb24BbQEN4CEgAAMh6EAXUHV0RG9jdW1lbnRBE2RvY3VtZW50c0ETbGVnYWN5LTQyAR3g4OAJAxHg4MEB4OA=";
    private const string MutationKind = "PutDocument";
    private const string Resource = "documents";
    private const string DocumentId = "legacy-42";
    private const string Fingerprint = "legacy-document-command-fingerprint";
    private const string PartitionId = "tenant/database/partition";
    private const string CommandId = "11111111-2222-3333-4444-555555555555";
    private const string Incarnation = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const long Revision = 7;
    private const long PolicyEpoch = 11;
    private const long Position = 27;
    private const long OwnershipEpoch = 3;

    [Test]
    public async Task AcComp006PreCompositionNativeBytesDecodeWithSafeDefaults()
    {
        var mutation = NativeSerialization.Deserialize<MutationReceipt>(Convert.FromBase64String(LegacyReceipt));
        var outcome = NativeSerialization.Deserialize<StoredOutcome>(Convert.FromBase64String(LegacyOutcome));
        var receipt = outcome.Result.Get<CommitReceipt>();

        await Assert.That(mutation.Kind).IsEqualTo(MutationKind);
        await Assert.That(mutation.Resource).IsEqualTo(Resource);
        await Assert.That(mutation.Id).IsEqualTo(DocumentId);
        await Assert.That(mutation.Revision).IsEqualTo(Revision);
        await Assert.That(mutation.CompositionReferences.IsDefault).IsFalse();
        await Assert.That(mutation.CompositionReferences.IsEmpty).IsTrue();
        await Assert.That(outcome.Fingerprint).IsEqualTo(Fingerprint);
        await Assert.That(outcome.Incarnation).IsEqualTo(Guid.Parse(Incarnation));
        await Assert.That(outcome.PolicyEpoch).IsEqualTo(PolicyEpoch);
        await Assert.That(outcome.BlobAuthority).IsNull();
        await Assert.That(outcome.CompositionAuthority).IsNull();
        await Assert.That(receipt.CommandId).IsEqualTo(Guid.Parse(CommandId));
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(Guid.Parse(Incarnation));
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(PartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(Position);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(OwnershipEpoch);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumDurable);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(1);
        await Assert.That(receipt.Mutations[0].Kind).IsEqualTo(MutationKind);
        await Assert.That(receipt.Mutations[0].Resource).IsEqualTo(Resource);
        await Assert.That(receipt.Mutations[0].Id).IsEqualTo(DocumentId);
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(Revision);
        await Assert.That(receipt.Mutations[0].CompositionReferences.IsDefault).IsFalse();
        await Assert.That(receipt.Mutations[0].CompositionReferences.IsEmpty).IsTrue();
    }
}
