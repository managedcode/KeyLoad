namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobPersistedStateTests
{
    [Test]
    [Arguments(BlobPersistedStateCase.BlobIdentity, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.UploadIdentity, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.EmptyUploadIdentity, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.EmptyBeginCommand, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.EmptyIntegrityIncarnation, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.EmptyCreator, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.MissingAccess, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.InvalidAccess, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.NegativeLength, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.ExcessiveLength, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.NegativeRevision, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.InvalidStatus, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.NegativeNextOrdinal, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.ExcessiveNextOrdinal, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.NegativeReclaimCursor, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.ExcessiveReclaimCursor, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.NegativeStoredBytes, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.ExcessiveStoredBytes, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.NegativeReservation, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.ExcessiveReservation, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.StoredByteEquation, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.RetiredActive, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.ActiveReclaimed, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.ActiveReservation, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.InactiveReservation, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.CompletePartCount, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.InvalidHash, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.InitialHashMismatch, ErrorCode.Corruption)]
    [Arguments(BlobPersistedStateCase.FencedIncarnation, ErrorCode.RecoveryRequired)]
    public async Task AcBlob004And007PersistedStateCorruptionFailsClosed(
        BlobPersistedStateCase testCase, ErrorCode expected)
    {
        using var database = new TestDatabase();
        var fixture = BlobPersistedCorruptionFixtureFactory.CreateState(database, testCase);
        BlobPersistedCorruptionAssertions.CorruptState(database, fixture, testCase);

        await BlobPersistedCorruptionAssertions.AssertStateRejectedAsync(database, fixture, expected);
    }

    [Test]
    [Arguments(BlobPersistedStateCase.MissingQuotaObject)]
    [Arguments(BlobPersistedStateCase.MissingQuotaVersion)]
    [Arguments(BlobPersistedStateCase.MissingQuotaReservation)]
    [Arguments(BlobPersistedStateCase.MissingQuotaUpload)]
    public async Task AcBlob005PersistedStateQuotaMismatchFailsClosed(BlobPersistedStateCase testCase)
    {
        using var database = new TestDatabase();
        var fixture = BlobPersistedCorruptionFixtureFactory.CreateState(database, testCase);
        BlobPersistedCorruptionAssertions.CorruptState(database, fixture, testCase);

        await BlobPersistedCorruptionAssertions.AssertStateRejectedAsync(database, fixture, ErrorCode.Corruption);
    }
}
