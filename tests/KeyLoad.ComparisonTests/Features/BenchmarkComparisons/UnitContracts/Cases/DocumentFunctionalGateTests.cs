using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-METH-004: native original TRX and ZIP evidence bind the exclusive RF3 functional gate.</summary>
internal sealed class DocumentFunctionalGateTests
{
    private const string Passed = "passed";
    private const string XmlRejected = "xmlRejected";
    private const string CaptureRejected = "captureRejected";
    private const string CompleteOriginalPassed = "completeOriginalPassed";
    private const string ChangedDigest = "changedDigest";
    private const string WrongServer = "wrongServer";
    private const string ChangedOriginal = "changedOriginal";

    [Test]
    public async Task AcMeth004FunctionalGateRequiresOnePassingOriginalTestAndSameSourceJobArtifactImages()
    {
        var script = Path.Combine(
            IsolatedAggregateNodeProcess.RepositoryRoot(),
            "tests",
            "KeyLoad.ComparisonTests",
            "Features",
            "BenchmarkComparisons",
            "UnitContracts",
            "Fixtures",
            "document-functional-probe.mjs"
        );
        var result = await IsolatedAggregateNodeProcess.RunAsync([script], TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Error).IsEqualTo(string.Empty);
        var value = JsonNode.Parse(result.Output)!.AsObject();
        await Assert.That(value[Passed]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(value[CompleteOriginalPassed]!.GetValue<bool>()).IsTrue();
        await Assert.That(value[XmlRejected]!.AsArray().Count).IsEqualTo(6);
        await Assert.That(value[XmlRejected]!.AsArray().All(item => item!.GetValue<bool>())).IsTrue();
        await Assert.That(value[CaptureRejected]!.AsArray().Count).IsEqualTo(8);
        await Assert.That(value[CaptureRejected]!.AsArray().All(item => item!.GetValue<bool>())).IsTrue();
        await Assert.That(value[ChangedDigest]!.GetValue<bool>()).IsTrue();
        await Assert.That(value[WrongServer]!.GetValue<bool>()).IsTrue();
        await Assert.That(value[ChangedOriginal]!.GetValue<bool>()).IsTrue();
    }
}
