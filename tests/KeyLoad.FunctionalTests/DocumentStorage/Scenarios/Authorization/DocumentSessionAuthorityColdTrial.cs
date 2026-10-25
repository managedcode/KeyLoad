using KeyLoad.FunctionalTests.Messaging;
using A = KeyLoad.FunctionalTests.DocumentStorage.DocumentSessionAuthorityColdAssertions;

namespace KeyLoad.FunctionalTests.DocumentStorage;

internal static class DocumentSessionAuthorityColdTrial
{
    internal static Task RunAsync(int damage) => DocumentPatchAtomicityOwner.RunAsync(async fixture =>
    {
        fixture.Source.Configure(A.Collection, ResourceKind.Collection);
        var receipt = fixture.Source.Commit(new PutDocument(A.Collection, A.Id, A.Json, 0));
        var originalCommand = fixture.Entry(receipt.CommandId).PreparedCommand;
        var originalImage = QueueWholeFlowStorage.Bytes(fixture.Store);
        var originalPosition = fixture.Store.Position;
        var original = DocumentSessionAuthorityFault.Original(fixture);
        DocumentSessionAuthorityFault.Damage(fixture, original, damage);
        var damagedImage = QueueWholeFlowStorage.Bytes(fixture.Store);
        var damagedPosition = checked(originalPosition + 1);
        await RefusedAsync(fixture, receipt.Token, damage);
        await DocumentSessionAuthorityColdAssertions.UnchangedAsync(fixture, damagedImage, damagedPosition);
        await fixture.ReopenAsync();
        await RefusedAsync(fixture, receipt.Token, damage);
        await DocumentSessionAuthorityColdAssertions.UnchangedAsync(fixture, damagedImage, damagedPosition);
        DocumentSessionAuthorityFault.Repair(fixture, original);
        await DocumentSessionAuthorityColdAssertions.UnchangedAsync(fixture, originalImage, checked(originalPosition + 2));
        await A.LiteralAsync(fixture, receipt.Token, 1);
        await A.ReplayAsync(fixture, originalCommand, receipt);
        await DocumentSessionAuthorityContinuation.RunAsync(fixture, originalCommand, receipt);
    });

    private static async Task RefusedAsync(DocumentPatchAtomicityOwner fixture, CommitToken token, int damage)
    {
        DocumentResult? actual = null;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => actual = A.Read(fixture, token));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(DocumentSessionAuthorityFault.Detail(damage));
        await Assert.That(actual).IsNull();
    }
}
