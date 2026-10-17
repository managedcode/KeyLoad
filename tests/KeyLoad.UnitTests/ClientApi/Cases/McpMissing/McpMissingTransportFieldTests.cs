using KeyLoad.Server;
using ModelContextProtocol.Protocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003/005: missing compared fields cannot reach native reflected-header errors.</summary>
internal sealed class McpMissingTransportFieldTests
{
    /// <summary>A present method with no body method uses the fixed safe transport failure.</summary>
    [Test]
    public async Task PresentMethodWithMissingBodyMethodUsesSafeValidation()
    {
        var wire = McpResponseBoundaryTestData.TransportFrame(null, null, omitParameters: true);
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(McpTransportGuardTestData.Marker, null), UnitMcpOptions.Execution());
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpTransportGuard.Inspect(wire, headers));
        await AssertSafeValidationAsync(error);
    }

    /// <summary>A present name rejects every absent or nonstring routed target before native header comparison.</summary>
    /// <param name="method">The native method selecting the name or URI parameter.</param>
    /// <param name="field">The routed target field.</param>
    /// <param name="includeMethod">Whether the optional HTTP method header is also present.</param>
    [Test]
    [Arguments(RequestMethods.ToolsCall, McpTransportGuardTestData.NameField, true)]
    [Arguments(RequestMethods.ToolsCall, McpTransportGuardTestData.NameField, false)]
    [Arguments(RequestMethods.PromptsGet, McpTransportGuardTestData.NameField, true)]
    [Arguments(RequestMethods.PromptsGet, McpTransportGuardTestData.NameField, false)]
    [Arguments(RequestMethods.ResourcesRead, McpTransportGuardTestData.UriField, true)]
    [Arguments(RequestMethods.ResourcesRead, McpTransportGuardTestData.UriField, false)]
    public async Task PresentNameWithMissingRoutedTargetUsesSafeValidation(string method, string field, bool includeMethod)
    {
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(
            includeMethod ? method : null, McpTransportGuardTestData.Marker), UnitMcpOptions.Execution());
        foreach (var fault in Enum.GetValues<McpMissingTargetFault>())
        {
            var wire = McpResponseBoundaryTestData.InvalidTargetFrame(method, field, fault);
            var error = Assert.ThrowsExactly<KeyLoadException>(() => McpTransportGuard.Inspect(wire, headers));
            await AssertSafeValidationAsync(error);
        }
    }

    /// <summary>Absent optional headers keep missing targets and parameters available for native validation.</summary>
    /// <param name="method">The native body method selecting the target.</param>
    /// <param name="field">The corresponding native target parameter key.</param>
    [Test]
    [Arguments(RequestMethods.ToolsCall, McpTransportGuardTestData.NameField)]
    [Arguments(RequestMethods.PromptsGet, McpTransportGuardTestData.NameField)]
    [Arguments(RequestMethods.ResourcesRead, McpTransportGuardTestData.UriField)]
    public async Task AbsentRoutingHeadersLeaveMissingFieldsToNative(string method, string field)
    {
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(null, null), UnitMcpOptions.Execution());
        foreach (var fault in Enum.GetValues<McpMissingTargetFault>())
        {
            var wire = McpResponseBoundaryTestData.InvalidTargetFrame(method, field, fault);
            var before = wire.ToArray();
            McpTransportGuard.Inspect(wire, headers);
            await Assert.That(wire.AsSpan().SequenceEqual(before)).IsTrue();
        }
        McpTransportGuard.Inspect(McpResponseBoundaryTestData.TransportFrame(null, null, omitParameters: true), headers);
        await Assert.That(headers.Method).IsNull();
        await Assert.That(headers.Name).IsNull();
    }

    private static async Task AssertSafeValidationAsync(KeyLoadException error)
    {
        var baseline = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.InvalidHeaders(McpTransportHeaderFault.MarkerRevision), UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(baseline.Message);
        await Assert.That(error.Message).DoesNotContain(McpTransportGuardTestData.Marker);
    }
}
