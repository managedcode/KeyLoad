namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCacheFixtureIdentityTests
{
    private const int SigningKeyLength = 32;
    private static readonly byte[] ValidSigningKey = Enumerable.Range(1, SigningKeyLength)
        .Select(value => (byte)value).ToArray();

    // AC-CACHE-015: absent, explicit valid and explicit empty keys retain distinct provider semantics.
    [Test]
    public async Task DefaultKeyPersistsOnReopenAndExplicitKeyPresenceIsPreserved()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            var defaultStore = fixture.OpenStore();
            var original = defaultStore.Identity;
            var defaultDirectory = fixture.DirectoryOf(defaultStore);
            await Assert.That(original.SigningKey.Length).IsEqualTo(SigningKeyLength);
            fixture.CloseStore(defaultStore);

            var defaultReopened = fixture.ReopenStore(defaultDirectory);
            await Assert.That(defaultReopened.Identity.NodeId).IsEqualTo(original.NodeId);
            await Assert.That(defaultReopened.Identity.Incarnation).IsEqualTo(original.Incarnation);
            await Assert.That(defaultReopened.Identity.SigningKey.Span.SequenceEqual(original.SigningKey.Span)).IsTrue();

            await VerifyValidCallerKeyAsync(fixture);
            await VerifyExplicitEmptyKeyIsRejectedAsync(fixture);
        });
    }

    private static async Task VerifyValidCallerKeyAsync(ZoneTreePointCacheFileFixture fixture)
    {
        var incarnation = Guid.NewGuid();
        var store = fixture.OpenStore(incarnation: incarnation, signingKey: ValidSigningKey);
        var sharedIncarnationPeer = fixture.OpenStore(incarnation: incarnation, signingKey: ValidSigningKey);
        await Assert.That(store.Identity.Incarnation).IsEqualTo(incarnation);
        await Assert.That(store.Identity.SigningKey.Span.SequenceEqual(ValidSigningKey)).IsTrue();
        await Assert.That(sharedIncarnationPeer.Identity.Incarnation).IsEqualTo(incarnation);
        await Assert.That(sharedIncarnationPeer.Identity.SigningKey.Span.SequenceEqual(ValidSigningKey)).IsTrue();
    }

    private static async Task VerifyExplicitEmptyKeyIsRejectedAsync(ZoneTreePointCacheFileFixture fixture)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.OpenStore(signingKey: []));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }
}
