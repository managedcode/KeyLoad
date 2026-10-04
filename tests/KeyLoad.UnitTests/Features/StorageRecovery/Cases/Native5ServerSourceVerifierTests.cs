using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class Native5ServerSourceVerifierTests
{
    [Test]
    public async Task AcEpoch010VerifierChecksGenuineSourceSidecarsWithControlledImageInput()
    {
        var results = await Native5ServerSourceVerifierNodeProcess.ProbeAsync(Native5ServerProofFixture.Valid());
        await Assert.That(results.Select(result => result.Name)).IsEquivalentTo(
            ExpectedCases, CollectionOrdering.Matching);
        var positive = results.Single(result => result.Name == GenuineEvidence);
        await Assert.That(positive).IsEqualTo(new Native5ServerSourceVerifierResult(GenuineEvidence, true, true, true));
        foreach (var name in ExportPositives)
        { await Assert.That(results.Single(result => result.Name == name).Accepted).IsTrue(); }
        var emptyDirectory = results.Single(result => result.Name == UnknownEmptyDirectory);
        await Assert.That(emptyDirectory.Accepted).IsFalse();
        await Assert.That(emptyDirectory.Error).IsEqualTo(InvalidExportError);
        foreach (var result in results.Where(result => !AcceptedCases.Contains(result.Name)))
        { await Assert.That(result.Accepted).IsFalse().Because(result.Name); }
        foreach (var result in results)
        { await Assert.That(result.Preserved).IsTrue().Because(result.Name); }
    }

    private static readonly string[] ExpectedCases =
    [
        GenuineEvidence,
        "actual prior source export matches pinned inventory",
        UnknownEmptyDirectory,
        "prior source export verifies after owned directory removal",
        "inventory row count mismatch",
        "inventory receipt hash mismatch",
        "inventory canonical order mismatch",
        "inventory transcript mismatch",
        "archive observed digest mismatch",
        "archive observed length mismatch",
        "oversized inventory sidecar",
        "oversized receipt sidecar",
        "manifest symlink preserved",
        "evidence directory symlink preserved"
    ];

    private static readonly string[] ExportPositives =
    [
        "actual prior source export matches pinned inventory",
        "prior source export verifies after owned directory removal"
    ];

    private static readonly string[] AcceptedCases = [GenuineEvidence, .. ExportPositives];
    private const string GenuineEvidence = "actual fixed source sidecars with controlled parser image input";
    private const string UnknownEmptyDirectory = "unknown empty source directory rejected exactly";
    private const string InvalidExportError = "The immutable native5 source export is invalid.";
}
