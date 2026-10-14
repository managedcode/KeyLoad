using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesResourceChunkPolicyRf3Refusal
{
    private const string FieldUseDetail = "The query requires a protected field-use grant.";
    private const string PrivateTagValue = "private-marker";

    internal static async Task DeniedAsync(RequestCqrsRf3Callers reader,
        ReadSampleChunkWindowRequest request, bool official, CancellationToken token)
    {
        if (official)
        {
            var reply = await reader.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, TimeSeriesRf3Scenario.PrivateTags, PrivateTagValue);
            return;
        }
        var failed = await reader.Sdk.ReadSampleChunkWindowAsync(request, token);
        await Assert.That(failed.IsFailed).IsTrue();
        await Assert.That(failed.Value).IsNull();
        await Assert.That(failed.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await Assert.That(failed.Problem?.Detail).IsEqualTo(FieldUseDetail);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, FieldUseDetail), failed.Problem);
    }
}
