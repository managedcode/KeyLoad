namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedProducerRejectionTests
{
    /// <summary>AC-ISO-008: native files and corruption copies prove preflight/raw rejection, never fabricated measurements.</summary>
    [Test]
    [Arguments("missing", "E_ISOLATED_EVIDENCE")]
    [Arguments("foreign", "E_ISOLATED_EVIDENCE")]
    [Arguments("duplicate", "E_ISOLATED_EVIDENCE")]
    [Arguments("expired", "E_ISOLATED_EVIDENCE")]
    [Arguments("failedJob", "E_ISOLATED_EVIDENCE")]
    [Arguments("options", "E_ISOLATED_EVIDENCE")]
    [Arguments("unsafeRawPath", "E_ISOLATED_EVIDENCE")]
    [Arguments("rawSymlink", "E_AGGREGATE_INPUT")]
    [Arguments("rawHash", "E_ISOLATED_EVIDENCE")]
    [Arguments("failed", "E_AGGREGATE_REPORT")]
    [Arguments("unixHost", "E_AGGREGATE_REPORT")]
    [Arguments("mixedDataset", "E_ISOLATED_EVIDENCE")]
    public async Task AC_ISO_008_ProducerRefusesInvalidParserInputsBeforeOutput(string corruption, string expectedError)
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var input = Path.Combine(temporary.Path, "validation-input");
        await SiteIsolatedProducerFixture.CreateAsync(fixture, input, corruption, token);
        var output = Path.Combine(temporary.Path, "projection.json");
        var response = await SiteIsolatedNodeProcess.RunAsync(fixture.Inputs.Site, new
        {
            operation = "produce",
            repository = fixture.Inputs.Site.Repository,
            input,
            output,
        }, token);
        await Assert.That(response.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsFalse();
        await Assert.That(response.GetProperty(SiteIsolatedFields.Error).GetString()).IsEqualTo(expectedError);
        await Assert.That(File.Exists(output)).IsFalse();
    }
}
