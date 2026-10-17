namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentSessionAuthorityColdTrial
{
    private const long OneCommit = 1;
    private const long DamageAndRepair = 2;

    internal static async Task RunAsync(RemoteDocumentNativeFixture fixture, int damage)
    {
        fixture.Seed();
        fixture.GrantDestination();
        await RemoteDocumentNativeAssertions.ReceiptAsync(fixture);
        var sourceImage = RemoteDocumentNativeAssertions.Image(fixture.Source);
        var sourcePosition = fixture.Source.Store.Position;
        var originalImage = RemoteDocumentNativeAssertions.Image(fixture.Destination);
        var originalPosition = fixture.Destination.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Destination.Store.Identity);
        var original = DocumentSessionAuthorityFault.Original(fixture);
        DocumentSessionAuthorityFault.Damage(fixture, original, damage);
        var damagedImage = RemoteDocumentNativeAssertions.Image(fixture.Destination);
        await DocumentSessionAuthorityColdAssertions.UnchangedAsync(fixture, sourceImage, sourcePosition,
            damagedImage, checked(originalPosition + OneCommit));
        await RefusedAsync(fixture, damage);
        fixture.Destination.Reopen();
        await Assert.That(NativeSerialization.Serialize(fixture.Destination.Store.Identity).SequenceEqual(identity)).IsTrue();
        await RefusedAsync(fixture, damage);
        await DocumentSessionAuthorityColdAssertions.UnchangedAsync(fixture, sourceImage, sourcePosition,
            damagedImage, checked(originalPosition + OneCommit));
        DocumentSessionAuthorityFault.Repair(fixture, original);
        await DocumentSessionAuthorityColdAssertions.UnchangedAsync(fixture, sourceImage, sourcePosition,
            originalImage, checked(originalPosition + DamageAndRepair));
        await DocumentSessionAuthorityColdAssertions.LiteralAsync(fixture, fixture.Receipt.Token,
            RemoteDocumentNativeFixture.First, fixture.Destination.Store.Position);
        await DocumentSessionAuthorityColdAssertions.ReplayAsync(fixture, fixture.Write, fixture.Receipt);
        await DocumentSessionAuthorityContinuation.RunAsync(fixture);
        await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Source).SequenceEqual(sourceImage, StringComparer.Ordinal)).IsTrue();
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(sourcePosition);
    }

    private static async Task RefusedAsync(RemoteDocumentNativeFixture fixture, int damage)
    {
        OwnedDocumentReadResultV1? actual = null;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => actual =
            DocumentSessionAuthorityColdAssertions.Read(fixture, fixture.Receipt.Token));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(DocumentSessionAuthorityFault.Detail(damage));
        await Assert.That(actual).IsNull();
    }
}
