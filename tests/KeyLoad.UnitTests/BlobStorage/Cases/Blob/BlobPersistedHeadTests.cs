namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobPersistedHeadTests
{
    [Test]
    [Arguments(BlobPersistedHeadCase.BlobIdentity, ErrorCode.Corruption)]
    [Arguments(BlobPersistedHeadCase.NegativeRevision, ErrorCode.Corruption)]
    [Arguments(BlobPersistedHeadCase.NegativeLength, ErrorCode.Corruption)]
    [Arguments(BlobPersistedHeadCase.ExcessiveLength, ErrorCode.Corruption)]
    [Arguments(BlobPersistedHeadCase.PartCount, ErrorCode.Corruption)]
    [Arguments(BlobPersistedHeadCase.PublishedEmptyVersion, ErrorCode.Corruption)]
    [Arguments(BlobPersistedHeadCase.PublishedZeroRevision, ErrorCode.Corruption)]
    [Arguments(BlobPersistedHeadCase.PublishedDeleted, ErrorCode.Corruption)]
    [Arguments(BlobPersistedHeadCase.PublishedMissingHash, ErrorCode.Corruption)]
    [Arguments(BlobPersistedHeadCase.PublishedInvalidHash, ErrorCode.Corruption)]
    [Arguments(BlobPersistedHeadCase.MissingAccess, ErrorCode.Corruption)]
    [Arguments(BlobPersistedHeadCase.InvalidAccess, ErrorCode.Corruption)]
    [Arguments(BlobPersistedHeadCase.FencedIncarnation, ErrorCode.RecoveryRequired)]
    [Arguments(BlobPersistedHeadCase.UnsupportedFormat, ErrorCode.FormatUnsupported)]
    public async Task AcBlob004And007PublishedHeadCorruptionFailsClosed(
        BlobPersistedHeadCase testCase, ErrorCode expected)
    {
        using var database = new TestDatabase();
        var fixture = BlobPersistedCorruptionFixtureFactory.CreateHead(database, testCase);
        BlobPersistedCorruptionAssertions.CorruptHead(database, fixture, testCase);

        await BlobPersistedCorruptionAssertions.AssertHeadRejectedAsync(database, fixture, expected);
    }

    [Test]
    [Arguments(BlobPersistedHeadCase.UnpublishedLength)]
    [Arguments(BlobPersistedHeadCase.UnpublishedPartCount)]
    [Arguments(BlobPersistedHeadCase.UnpublishedHash)]
    [Arguments(BlobPersistedHeadCase.UnpublishedLiveRevision)]
    public async Task AcBlob001And007UnpublishedHeadCannotExposePartialState(BlobPersistedHeadCase testCase)
    {
        using var database = new TestDatabase();
        var fixture = BlobPersistedCorruptionFixtureFactory.CreateHead(database, testCase);
        BlobPersistedCorruptionAssertions.CorruptHead(database, fixture, testCase);

        await BlobPersistedCorruptionAssertions.AssertHeadRejectedAsync(database, fixture, ErrorCode.Corruption);
    }
}
