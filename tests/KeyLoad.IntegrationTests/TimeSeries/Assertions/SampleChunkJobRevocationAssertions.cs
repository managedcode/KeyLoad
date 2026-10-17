using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkJobRevocationAssertions
{
    internal static async Task UnchangedAsync(SampleChunkJobRevocationScenario scenario, KeyLoadClient administrator,
        CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadSampleChunkWindowAsync(
            scenario.Window.Request, token).ConfigureAwait(false));
        var original = scenario.Correction ?? throw new InvalidOperationException(SampleChunkJobRevocationProtocol.Missing);
        await Assert.That(actual.CutPosition >= original.Token.Position).IsTrue();
        var expected = new SampleChunkWindowResult(scenario.Window.WindowId, scenario.Window.From,
            scenario.Window.Until, SampleChunkRf3Protocol.SealedGeneration, SampleChunkRf3Protocol.CorrectedRevision,
            SampleChunkRf3Protocol.CorrectedSequence, null, [.. scenario.Window.Expected], actual.CutPosition);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
    }

    internal static async Task HealthyAsync(SampleChunkJobRevocationScenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        var minimum = scenario.Correction ?? throw new InvalidOperationException(SampleChunkJobRevocationProtocol.Missing);
        await SampleChunkRf3Assertions.AllReadRoutesAsync(scenario.Window, sdk, mcp, minimum, token);
        await SampleChunkRf3Assertions.OriginalReceiptAsync(sdk, mcp,
            scenario.OriginalSeal ?? throw new InvalidOperationException(SampleChunkJobRevocationProtocol.Missing),
            scenario.OriginalReceipt ?? throw new InvalidOperationException(SampleChunkJobRevocationProtocol.Missing), token);
        await SampleChunkRf3Assertions.AllReadRoutesAsync(scenario.Window, sdk, mcp, minimum, token);
    }
}
