using System.Text.Json;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class Native5ServerProofParserTests
{
    [Test]
    public async Task AcEpoch010SyntheticParserInputBindsCurrentProducerToDistinctPriorSource()
    {
        var valid = Native5ServerProofFixture.Valid();
        var cases = new[]
        {
            valid,
            Native5ServerProofMutationFixture.ArchiveObservation(valid),
            Native5ServerProofMutationFixture.OptionalManifestMediaType(valid),
            Native5ServerProofMutationFixture.DockerManifest(),
            Native5ServerProofMutationFixture.OciManifestWithLayerType(Native5ServerProofFixture.OciLayerContentType,
                "valid OCI uncompressed layer"),
            Native5ServerProofMutationFixture.OciManifestWithLayerType(Native5ServerProofFixture.OciZstdLayerContentType,
                "valid OCI zstd layer"),
            Native5ServerProofMutationFixture.AnnotationBoundary(),
            Native5ServerProofMutationFixture.AnnotationByteBoundary(),
            Native5ServerProofMutationFixture.WithLayerCount(valid, 1024, "valid maximum layer count")
        };
        var results = await Native5ServerProofNodeProcess.ProbeAsync(cases);
        await Assert.That(results.Count).IsEqualTo(cases.Length);
        await Assert.That(results).IsEquivalentTo(
            cases.Select(item => new Native5ServerProofProbeResult(true, item.Name)), CollectionOrdering.Matching);
        using var producer = JsonDocument.Parse(valid.ExpectedProducer);
        using var receipt = JsonDocument.Parse(valid.Receipt);
        var producerSha = producer.RootElement.GetProperty(Native5ServerProofFixture.SourceSha).GetString();
        var priorSha = receipt.RootElement.GetProperty(Native5ServerProofFixture.ImageSource)
            .GetProperty(Native5ServerProofFixture.Revision).GetString();
        await Assert.That(producerSha).IsEqualTo(Native5ServerProofFixture.CurrentProducerSha);
        await Assert.That(priorSha).IsEqualTo(Native5ServerProofFixture.PriorRevision);
        await Assert.That(producerSha).IsNotEqualTo(priorSha);
    }

    [Test]
    public async Task AcEpoch010RejectsProducerRunAndTargetMixing()
        => await AssertRejectedAsync(Native5ServerProofCaseCatalog.ProducerFailures());

    [Test]
    public async Task AcEpoch010RejectsPriorSourceOverlayAndPinnedBaseDrift()
        => await AssertRejectedAsync(Native5ServerProofCaseCatalog.SourceAndBaseFailures());

    [Test]
    public async Task AcEpoch010RejectsImageManifestAndReferenceDrift()
        => await AssertRejectedAsync(Native5ServerProofCaseCatalog.ImageFailures());

    [Test]
    public async Task AcEpoch010RejectsUnknownMalformedDuplicateAndOverBoundInputs()
        => await AssertRejectedAsync(Native5ServerProofCaseCatalog.ShapeFailures());

    private static async Task AssertRejectedAsync(IReadOnlyList<Native5ServerParserCase> cases)
    {
        var results = await Native5ServerProofNodeProcess.ProbeAsync(cases);
        await Assert.That(results.Select(result => result.Name)).IsEquivalentTo(
            cases.Select(item => item.Name), CollectionOrdering.Matching);
        foreach (var result in results)
        { await Assert.That(result.Accepted).IsFalse().Because(result.Name); }
    }
}
