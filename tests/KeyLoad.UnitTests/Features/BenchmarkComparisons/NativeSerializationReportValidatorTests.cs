namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-IS-PERF-003: complete pinned exporter schemas admit; incomplete or tampered inputs reject.</summary>
internal sealed class NativeSerializationReportValidatorTests
{
    [Test]
    [Arguments(6, true)]
    [Arguments(5, false)]
    [Arguments(1, true)]
    public async Task CompleteSchemasRetainOutlierReducedResultsAndZeroAllocation(int resultCount, bool zeroAllocation)
    {
        var result = await NativeSerializationReportNodeProcess.ProbeAsync(NativeSerializationReportData.Reports(resultCount, zeroAllocation));
        await AssertProbeAsync(result, accepted: true);
    }

    [Test]
    [Arguments("missing-report")]
    [Arguments("missing-cell")]
    [Arguments("duplicate-cell")]
    [Arguments("wrong-type")]
    [Arguments("wrong-method")]
    [Arguments("wrong-size")]
    [Arguments("wrong-settings")]
    [Arguments("wrong-runtime-setting")]
    [Arguments("in-process-setting")]
    [Arguments("duplicate-parameter")]
    [Arguments("missing-warmup")]
    [Arguments("missing-actual")]
    [Arguments("missing-result")]
    [Arguments("duplicate-iteration")]
    [Arguments("wrong-launch")]
    [Arguments("zero-time")]
    [Arguments("zero-operations")]
    [Arguments("null-statistics")]
    [Arguments("wrong-n")]
    [Arguments("wrong-original-values")]
    [Arguments("negative-deviation")]
    [Arguments("negative-allocation")]
    [Arguments("missing-memory")]
    [Arguments("failed-generated-child")]
    [Arguments("wrong-bdn-version")]
    [Arguments("wrong-runtime")]
    [Arguments("wrong-os")]
    [Arguments("mixed-host")]
    public async Task IncompleteDuplicateOrTamperedSchemaFailsClosed(string fault)
    {
        var reports = NativeSerializationReportData.Reports();
        NativeSerializationReportMutations.Apply(reports, fault);
        var result = await NativeSerializationReportNodeProcess.ProbeAsync(reports);
        await AssertProbeAsync(result, accepted: false);
    }

    [Test]
    public async Task NonfiniteStatisticsCannotQualifyTheReport()
    {
        var result = await NativeSerializationReportNodeProcess.ProbeAsync(NativeSerializationReportData.Reports(), nonfinite: true);
        await AssertProbeAsync(result, accepted: false);
    }

    [Test]
    [Arguments("valid")]
    [Arguments("wrong-fixture")]
    [Arguments("wrong-size")]
    [Arguments("wrong-hash")]
    [Arguments("different-content")]
    [Arguments("empty-native")]
    [Arguments("undersized-native")]
    [Arguments("extra-field")]
    public async Task CorpusIdentityAndByteFactsAreClosed(string fault)
    {
        var corpus = NativeSerializationReportData.Corpus();
        NativeSerializationReportMutations.Corpus(corpus, fault);
        var result = await NativeSerializationReportNodeProcess.ProbeAsync(corpus, corpus: true);
        await AssertProbeAsync(result, accepted: fault == "valid");
    }

    private static async Task AssertProbeAsync(NativeSerializationReportResponse result, bool accepted)
    {
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardError).IsEqualTo(string.Empty);
        await Assert.That(result.Unchanged).IsTrue();
        await Assert.That(result.Accepted).IsEqualTo(accepted);
        await Assert.That(string.IsNullOrEmpty(result.Error)).IsEqualTo(accepted);
    }
}
