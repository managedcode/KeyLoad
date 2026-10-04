namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedOriginalReportRejectionTests
{
    private const string ErrorCode = "E_ISOLATED_EVIDENCE";

    /// <summary>AC-BC-FAIL-003: original measured branches retain corruption regressions without rebinding them to the current producer.</summary>
    [Test]
    [Arguments("missingCase")]
    [Arguments("duplicateRepetition")]
    [Arguments("caseStatus")]
    [Arguments("dataset")]
    [Arguments("provenance")]
    [Arguments("topology")]
    [Arguments("copies")]
    [Arguments("host")]
    [Arguments("options")]
    [Arguments("percentiles")]
    [Arguments("order")]
    [Arguments("throughput")]
    [Arguments("failures")]
    [Arguments("resources")]
    [Arguments("samples")]
    [Arguments("nullCorpus")]
    [Arguments("reportNull")]
    [Arguments("detail")]
    [Arguments("target")]
    [Arguments("run")]
    [Arguments("schema")]
    public async Task AC_BC_FAIL_003_OriginalReportRejectsMalformedMeasuredCopies(string corruption)
    {
        foreach (var queue in new[] { false, true })
        {
            await AssertRejectedAsync(corruption, queue);
        }
    }

    /// <summary>AC-BC-FAIL-003: genuine queue metrics retain completed-message and stage-latency rejection paths.</summary>
    [Test]
    [Arguments("queueCompleted")]
    [Arguments("queueEnqueue")]
    [Arguments("queueReceive")]
    [Arguments("queueAck")]
    public async Task AC_BC_FAIL_003_OriginalQueueRejectsMalformedCompletionAndStageMetrics(string corruption)
        => await AssertRejectedAsync(corruption, true);

    private static async Task AssertRejectedAsync(string corruption, bool queue)
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await SiteIsolatedOriginalReportFixture.ReadAsync(inputs, token, queue);
        var response = await SiteIsolatedOriginalReportFixture.ProbeAsync(inputs, corruption,
            SiteIsolatedOriginalReportFixture.MedianSelection, SiteIsolatedOriginalReportFixture.DefaultMetric, token, queue);
        await Assert.That(response.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsFalse();
        await Assert.That(response.GetProperty(SiteIsolatedFields.Error).GetString()).IsEqualTo(ErrorCode);
        await SiteIsolatedOriginalReportFixture.ReadAsync(inputs, token, queue);
    }
}
