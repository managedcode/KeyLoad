using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkMultiblockPagedRetentionOperations
{
    internal const int DeletePage = 128;
    internal const long ExpiredCount = 256;
    private const long CorrectedRevision = 259;
    private const long HealthyCorrectionRevision = 261;

    internal static void Seed(SampleChunkPartialRetentionFixture fixture)
    {
        fixture.Commit(new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until));
        fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SampleChunkCanonicalFixture.Initial, SampleChunkCanonicalFixture.Tags));
        fixture.Commit(new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, SampleChunkCanonicalFixture.AppendedRevision));
        fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [SampleChunkCanonicalFixture.Late], SampleChunkCanonicalFixture.Tags));
        fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            ChunkMultiblockBoundaryData.Additional, SampleChunkCanonicalFixture.Tags));
        fixture.Commit(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, CorrectedRevision));
    }

    internal static (Command<DatabaseCommand> Operation, CommitReceipt Receipt) Expire(SampleChunkPartialRetentionFixture fixture)
    {
        var operation = fixture.Operation(new ExpireSamples(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Late.Timestamp, DeletePage));
        return (operation, fixture.Apply(operation).Get<CommitReceipt>());
    }

    internal static void Healthy(SampleChunkPartialRetentionFixture fixture)
    {
        fixture.Commit(new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [ChunkSelectedIntegrityFixture.Fresh], SampleChunkCanonicalFixture.Tags));
        fixture.Commit(new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, HealthyCorrectionRevision));
    }
}
