namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobRangeAdmissionAssertions
{
    private const int NegativeBoundary = -1;
    private const int BeyondBoundary = 1;
    private const string InvalidDetail = "The blob request or policy is invalid.";
    private const string RevisionDetail = "The blob head revision changed.";

    internal static async Task RefusedThenHealthy(global::KeyLoad.Core.DatabaseEngine database,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, BlobRangeIntegrityFixture fixture)
    {
        var position = store.Position;
        var image = QueueWholeFlowStorage.Bytes(store);
        var operations = new global::KeyLoad.Core.Features.BlobStorage.BlobStorageOperations(database);
        BlobReadRequest[] invalid =
        [
            new(fixture.Blob, BlobRangeIntegrityFixture.InitialRevision,
                BlobRangeIntegrityFixture.FirstOrdinal, BlobRangeIntegrityFixture.SingleCount),
            new(fixture.Blob, BlobRangeIntegrityFixture.Revision, NegativeBoundary,
                BlobRangeIntegrityFixture.SingleCount),
            new(fixture.Blob, BlobRangeIntegrityFixture.Revision,
                BlobRangeIntegrityFixture.FirstOrdinal, NegativeBoundary),
            new(fixture.Blob, BlobRangeIntegrityFixture.Revision, BlobRangeIntegrityFixture.FirstOrdinal,
                BlobRangeIntegrityFixture.RawPartBytes + BeyondBoundary),
            new(fixture.Blob, BlobRangeIntegrityFixture.Revision,
                BlobRangeIntegrityFixture.PayloadBytes + BeyondBoundary, BlobRangeIntegrityFixture.EmptyCount),
            new(fixture.Blob, BlobRangeIntegrityFixture.Revision,
                BlobRangeIntegrityFixture.PayloadBytes, BlobRangeIntegrityFixture.SingleCount),
            new(fixture.Blob, BlobRangeIntegrityFixture.Revision, long.MaxValue,
                BlobRangeIntegrityFixture.SingleCount)
        ];
        foreach (var request in invalid)
        {
            await Refused(operations, request, ErrorCode.Validation, InvalidDetail);
            await BlobWholeFlowAssertions.Unchanged(store, position, image);
        }
        await Refused(operations, new(fixture.Blob, BlobRangeIntegrityFixture.Revision + BeyondBoundary,
            BlobRangeIntegrityFixture.CrossOffset, BlobRangeIntegrityFixture.RawPartBytes),
            ErrorCode.RevisionConflict, RevisionDetail);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
        var crossing = new BlobReadRequest(fixture.Blob, BlobRangeIntegrityFixture.Revision,
            BlobRangeIntegrityFixture.CrossOffset, BlobRangeIntegrityFixture.RawPartBytes);
        using var selected = new CancellationTokenSource();
        await selected.CancelAsync();
        var cancelled = Assert.ThrowsExactly<OperationCanceledException>(() =>
            operations.Read(BlobRangeIntegrityFixture.Principal, crossing, selected.Token));
        await Assert.That(cancelled.CancellationToken).IsEqualTo(selected.Token);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
        var healthy = operations.Read(BlobRangeIntegrityFixture.Principal, crossing);
        var expected = new BlobReadResult(fixture.Metadata, BlobRangeIntegrityFixture.CrossOffset,
            BlobRangeIntegrityFixture.Payload.AsMemory(BlobRangeIntegrityFixture.CrossOffset,
                BlobRangeIntegrityFixture.RawPartBytes));
        await BlobWholeFlowAssertions.Exact(healthy, expected);
        await BlobWholeFlowAssertions.Unchanged(store, position, image);
    }

    private static async Task Refused(global::KeyLoad.Core.Features.BlobStorage.BlobStorageOperations operations,
        BlobReadRequest request, ErrorCode code, string detail)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            operations.Read(BlobRangeIntegrityFixture.Principal, request));
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(failure.Message).IsEqualTo(detail);
    }
}
