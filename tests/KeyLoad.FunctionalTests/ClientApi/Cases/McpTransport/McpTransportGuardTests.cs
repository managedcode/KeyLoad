using KeyLoad.Server;
using ModelContextProtocol.Protocol;

namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>AC-MCP-002/003/004: concrete headers are bounded and cannot trigger reflective execution mismatch errors.</summary>
internal sealed class McpTransportGuardTests
{
    private const int MaximumMethodCharacters = 64;
    private const int MaximumNameCharacters = 256;

    /// <summary>Optional routing headers are trimmed while the exact protocol revision is retained.</summary>
    [Test]
    public async Task PresentRoutingHeadersAreTrimmed()
    {
        var headers = McpTransportGuardTestData.Headers(
            McpTransportGuardTestData.Whitespace + RequestMethods.ToolsCall + McpTransportGuardTestData.Whitespace,
            McpTransportGuardTestData.Whitespace + McpTransportGuardTestData.Target + McpTransportGuardTestData.Whitespace);
        var result = McpTransportGuard.ReadHeaders(headers, UnitMcpOptions.Execution());
        await Assert.That(result.Method).IsEqualTo(RequestMethods.ToolsCall);
        await Assert.That(result.Name).IsEqualTo(McpTransportGuardTestData.Target);
        await Assert.That(headers[McpTransportGuardTestData.RevisionHeader].ToString()).IsEqualTo(McpTransportGuardTestData.Revision);
    }

    /// <summary>Absent optional routing headers remain absent for execution mandatory-header validation.</summary>
    [Test]
    public async Task MissingRoutingHeadersRemainNullable()
    {
        var result = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(null, null), UnitMcpOptions.Execution());
        await Assert.That(result.Method).IsNull();
        await Assert.That(result.Name).IsNull();
    }

    /// <summary>Horizontal tabs inside bounded routing values remain permitted by the wire-boundary contract.</summary>
    [Test]
    public async Task InternalHorizontalTabsRemainPermitted()
    {
        var result = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(
            McpTransportGuardTestData.TabbedMethod, McpTransportGuardTestData.TabbedName), UnitMcpOptions.Execution());
        await Assert.That(result.Method).IsEqualTo(McpTransportGuardTestData.TabbedMethod);
        await Assert.That(result.Name).IsEqualTo(McpTransportGuardTestData.TabbedName);
    }

    /// <summary>Execution header dictionaries preserve case-insensitive protocol-header lookup.</summary>
    [Test]
    public async Task ProtocolHeaderNamesAreCaseInsensitive()
    {
        var headers = McpTransportGuardTestData.Headers();
        headers.Remove(McpTransportGuardTestData.RevisionHeader);
        headers[McpTransportGuardTestData.RevisionHeader.ToUpperInvariant()] = McpTransportGuardTestData.Revision;
        var result = McpTransportGuard.ReadHeaders(headers, UnitMcpOptions.Execution());
        await Assert.That(result.Method).IsEqualTo(RequestMethods.ToolsCall);
    }

    /// <summary>Invalid header variants use one fixed Validation detail and never echo private markers.</summary>
    /// <param name="fault">The concrete header mutation.</param>
    [Test]
    [Arguments(McpTransportHeaderFault.MissingRevision)]
    [Arguments(McpTransportHeaderFault.EmptyRevision)]
    [Arguments(McpTransportHeaderFault.LegacyRevision)]
    [Arguments(McpTransportHeaderFault.RevisionWhitespace)]
    [Arguments(McpTransportHeaderFault.MarkerRevision)]
    [Arguments(McpTransportHeaderFault.DuplicateRevision)]
    [Arguments(McpTransportHeaderFault.Session)]
    [Arguments(McpTransportHeaderFault.EmptySession)]
    [Arguments(McpTransportHeaderFault.LastEvent)]
    [Arguments(McpTransportHeaderFault.EmptyLastEvent)]
    [Arguments(McpTransportHeaderFault.DuplicateMethod)]
    [Arguments(McpTransportHeaderFault.DuplicateName)]
    [Arguments(McpTransportHeaderFault.EmptyMethod)]
    [Arguments(McpTransportHeaderFault.EmptyName)]
    [Arguments(McpTransportHeaderFault.EmptyMethodValues)]
    [Arguments(McpTransportHeaderFault.EmptyNameValues)]
    [Arguments(McpTransportHeaderFault.NonAsciiMethod)]
    [Arguments(McpTransportHeaderFault.NonAsciiName)]
    [Arguments(McpTransportHeaderFault.ControlMethod)]
    [Arguments(McpTransportHeaderFault.ControlName)]
    [Arguments(McpTransportHeaderFault.DeleteMethod)]
    [Arguments(McpTransportHeaderFault.DeleteName)]
    public async Task InvalidHeaderUsesFixedSafeValidation(McpTransportHeaderFault fault)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.InvalidHeaders(fault), UnitMcpOptions.Execution()));
        var baseline = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.InvalidHeaders(McpTransportHeaderFault.MarkerRevision), UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(baseline.Message);
        await Assert.That(error.Message).DoesNotContain(McpTransportGuardTestData.Marker);
    }

    /// <summary>Present routing-header length ceilings are inclusive and reject the next character.</summary>
    /// <param name="method">Whether to check the method rather than target-name ceiling.</param>
    /// <param name="maximum">The frozen inclusive character limit.</param>
    [Test]
    [Arguments(true, MaximumMethodCharacters)]
    [Arguments(false, MaximumNameCharacters)]
    public async Task RoutingHeaderLengthHasExactBoundary(bool method, int maximum)
    {
        var exact = new string('a', maximum);
        var headers = McpTransportGuardTestData.Headers();
        var field = method ? McpTransportGuardTestData.MethodHeader : McpTransportGuardTestData.NameHeader;
        headers[field] = exact;
        var parsed = McpTransportGuard.ReadHeaders(headers, UnitMcpOptions.Execution());
        await Assert.That(method ? parsed.Method : parsed.Name).IsEqualTo(exact);

        headers[field] = exact + 'a';
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpTransportGuard.ReadHeaders(headers, UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
    }
}
