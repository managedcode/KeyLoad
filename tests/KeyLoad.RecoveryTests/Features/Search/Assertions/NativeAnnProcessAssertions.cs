using System.Text.Json;
using KeyLoad.CrashHost.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class NativeAnnProcessAssertions
{
    private const int Count = 3;
    private const string SpaceId = "ann-process-space";
    private const string Model = "ann-process-model";
    private const string ModelVersion = "v1";
    private const int First = 0;
    private const long Revision = 1;
    private const long SequenceStep = 1;
    private static readonly string[] Ids = ["ann-process-00", "ann-process-01", "ann-process-02"];
    private static readonly float[][] InitialValues = [[0.25f, 1f, -0.5f], [1.25f, 1f, -0.5f], [2.25f, 1f, -0.5f]];
    private static readonly float[] HealthyValues = [11.25f, -2.5f, 0.75f];

    internal static async Task VerifyStageAsync(string root, string mode, CancellationToken token)
    {
        if (mode is NativeAnnCrashContract.Prepare or NativeAnnCrashContract.Fault)
        { return; }
        var recovered = await ReadAsync(root, NativeAnnCrashContract.RecoveredFile, token);
        var healthy = await ReadAsync(root, NativeAnnCrashContract.HealthyFile, token);
        await AssertLiteralAsync(recovered, healthy: false);
        await AssertLiteralAsync(healthy, healthy: true);
        await Assert.That(healthy.Source.ThroughSequence).IsEqualTo(recovered.Source.ThroughSequence + SequenceStep);
        await Assert.That(healthy.Source.CorpusSha256).IsNotEqualTo(recovered.Source.CorpusSha256);
        await Assert.That(healthy.IndexSha256).IsNotEqualTo(recovered.IndexSha256);
        if (mode == NativeAnnCrashContract.Verify)
        {
            var verified = await ReadAsync(root, NativeAnnCrashContract.VerifiedFile, token);
            await AssertLiteralAsync(verified, healthy: true);
            await Assert.That(verified.IndexSha256).IsEqualTo(healthy.IndexSha256);
            await Assert.That(verified.Receipt).IsEqualTo(healthy.Receipt);
            await Assert.That(verified.CanonicalImage).IsEqualTo(healthy.CanonicalImage);
            await Assert.That(verified.Position).IsEqualTo(healthy.Position);
            await Assert.That(verified.Source.CorpusSha256).IsEqualTo(healthy.Source.CorpusSha256);
            await Assert.That(verified.Source.ThroughSequence).IsEqualTo(healthy.Source.ThroughSequence);
        }
    }
    private static async Task AssertLiteralAsync(NativeAnnCrashSnapshot snapshot, bool healthy)
    {
        await Assert.That(snapshot.Records.Length).IsEqualTo(Count);
        for (var index = First; index < Count; index++)
        {
            var record = snapshot.Records[index];
            await Assert.That(record.DocumentId).IsEqualTo(Ids[index]);
            await Assert.That(record.Field).IsEqualTo(NativeAnnCrashContract.Field);
            await Assert.That(record.Space).IsEqualTo(new VectorSpace(SpaceId, Count, DistanceMetric.Cosine, Model, ModelVersion));
            await Assert.That(record.DocumentRevision).IsEqualTo(Revision);
            await Assert.That(record.Values).IsEquivalentTo(healthy && index == First ? HealthyValues : InitialValues[index], CollectionOrdering.Matching);
        }
        await Assert.That(snapshot.IndexSha256).IsNotEmpty();
        await Assert.That(snapshot.Receipt).IsNotEmpty();
        await Assert.That(snapshot.CanonicalImage).IsNotEmpty();
    }
    private static async Task<NativeAnnCrashSnapshot> ReadAsync(string root, string file, CancellationToken token)
        => JsonSerializer.Deserialize<NativeAnnCrashSnapshot>(await File.ReadAllTextAsync(Path.Combine(root, file), token), JsonDefaults.Options)
            ?? throw new InvalidOperationException(NativeAnnCrashContract.Invalid);
}
