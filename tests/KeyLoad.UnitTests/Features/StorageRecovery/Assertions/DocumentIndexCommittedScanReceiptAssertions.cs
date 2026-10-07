namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class DocumentIndexCommittedScanReceiptAssertions
{
    private const string PutDocumentKind = "putDocument";
    private const string DeleteDocumentKind = "deleteDocument";
    internal static async Task AssertWriterReceiptAsync(CommitReceipt receipt, long position)
    {
        await Assert.That(receipt.CommandId).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.Position).IsEqualTo(position);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(3);
        await AssertMutationAsync(receipt.Mutations[0], PutDocumentKind, DocumentIndexCommittedScanOracle.ReplaceId, DocumentIndexCommittedScanOracle.UpdatedRevision);
        await AssertMutationAsync(receipt.Mutations[1], DeleteDocumentKind, DocumentIndexCommittedScanOracle.DeleteId, DocumentIndexCommittedScanOracle.UpdatedRevision);
        await AssertMutationAsync(receipt.Mutations[2], PutDocumentKind, DocumentIndexCommittedScanOracle.InsertId, DocumentIndexCommittedScanOracle.InitialRevision);
    }

    private static async Task AssertMutationAsync(MutationReceipt actual, string kind, string id, long revision)
    {
        await Assert.That(actual.Kind).IsEqualTo(kind);
        await Assert.That(actual.Resource).IsEqualTo(DocumentIndexCommittedScanOracle.Collection);
        await Assert.That(actual.Id).IsEqualTo(id);
        await Assert.That(actual.Revision).IsEqualTo(revision);
    }
}
