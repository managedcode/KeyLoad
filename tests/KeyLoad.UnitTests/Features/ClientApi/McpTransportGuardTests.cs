using KeyLoad.Server;
using ModelContextProtocol.Protocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-002/003/004: concrete headers are bounded and cannot trigger reflective native mismatch errors.</summary>
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
        var result = McpTransportGuard.ReadHeaders(headers);
        await Assert.That(result.Method).IsEqualTo(RequestMethods.ToolsCall);
        await Assert.That(result.Name).IsEqualTo(McpTransportGuardTestData.Target);
        await Assert.That(headers[McpTransportGuardTestData.RevisionHeader].ToString()).IsEqualTo(McpTransportGuardTestData.Revision);
    }

    /// <summary>Absent optional routing headers remain absent for native mandatory-header validation.</summary>
    [Test]
    public async Task MissingRoutingHeadersRemainNullable()
    {
        var result = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(null, null));
        await Assert.That(result.Method).IsNull();
        await Assert.That(result.Name).IsNull();
    }

    /// <summary>Horizontal tabs inside bounded routing values remain permitted by the wire-boundary contract.</summary>
    [Test]
    public async Task InternalHorizontalTabsRemainPermitted()
    {
        var result = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(
            McpTransportGuardTestData.TabbedMethod, McpTransportGuardTestData.TabbedName));
        await Assert.That(result.Method).IsEqualTo(McpTransportGuardTestData.TabbedMethod);
        await Assert.That(result.Name).IsEqualTo(McpTransportGuardTestData.TabbedName);
    }

    /// <summary>Native header dictionaries preserve case-insensitive protocol-header lookup.</summary>
    [Test]
    public async Task ProtocolHeaderNamesAreCaseInsensitive()
    {
        var headers = McpTransportGuardTestData.Headers();
        headers.Remove(McpTransportGuardTestData.RevisionHeader);
        headers[McpTransportGuardTestData.RevisionHeader.ToUpperInvariant()] = McpTransportGuardTestData.Revision;
        var result = McpTransportGuard.ReadHeaders(headers);
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
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.InvalidHeaders(fault)));
        var baseline = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.InvalidHeaders(McpTransportHeaderFault.MarkerRevision)));
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
        var parsed = McpTransportGuard.ReadHeaders(headers);
        await Assert.That(method ? parsed.Method : parsed.Name).IsEqualTo(exact);

        headers[field] = exact + 'a';
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpTransportGuard.ReadHeaders(headers));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
    }
}

/// <summary>AC-MCP-002/003/004: actual framing-checked UTF-8 is matched without inspecting business authority.</summary>
internal sealed class McpTransportGuardInspectionTests
{
    /// <summary>A present target is selected by the body method even when the HTTP method header is absent.</summary>
    /// <param name="method">The native method that selects name or URI.</param>
    /// <param name="field">The native target parameter key.</param>
    /// <param name="target">The exact target value.</param>
    [Test]
    [Arguments(RequestMethods.ToolsCall, McpTransportGuardTestData.NameField, McpTransportGuardTestData.Target)]
    [Arguments(RequestMethods.PromptsGet, McpTransportGuardTestData.NameField, McpTransportGuardTestData.Target)]
    [Arguments(RequestMethods.ResourcesRead, McpTransportGuardTestData.UriField, McpTransportGuardTestData.Resource)]
    public async Task BodyMethodSelectsPresentNameHeader(string method, string field, string target)
    {
        var wire = McpTransportGuardTestData.Frame(method, new() { [field] = target });
        var snapshot = wire.ToArray();
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(null, target));
        McpTransportGuard.Inspect(wire, headers);
        await Assert.That(wire.AsSpan().SequenceEqual(snapshot)).IsTrue();
        await Assert.That(McpTransportGuardTestData.ReadTarget(wire, field)).IsEqualTo(target);
    }

    /// <summary>A mismatched target is rejected according to the body-selected field even without a method header.</summary>
    /// <param name="method">The native body method that selects the target field.</param>
    /// <param name="field">The native target parameter key.</param>
    [Test]
    [Arguments(RequestMethods.ToolsCall, McpTransportGuardTestData.NameField)]
    [Arguments(RequestMethods.PromptsGet, McpTransportGuardTestData.NameField)]
    [Arguments(RequestMethods.ResourcesRead, McpTransportGuardTestData.UriField)]
    public async Task BodySelectedTargetMismatchUsesSafeValidation(string method, string field)
    {
        var wire = McpTransportGuardTestData.Frame(method, new() { [field] = McpTransportGuardTestData.Marker });
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(null, McpTransportGuardTestData.Target));
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpTransportGuard.Inspect(wire, headers));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).DoesNotContain(McpTransportGuardTestData.Marker);
    }

    /// <summary>Absent or unrouted body methods do not invent target routing ahead of native protocol validation.</summary>
    /// <param name="missingMethod">Whether to omit the body method rather than use a method without target routing.</param>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task UnroutedBodyMethodLeavesNativeValidationIntact(bool missingMethod)
    {
        var method = missingMethod ? null : RequestMethods.ServerDiscover;
        var wire = McpTransportGuardTestData.Frame(method, new() { [McpTransportGuardTestData.NameField] = McpTransportGuardTestData.Marker });
        var snapshot = wire.ToArray();
        McpTransportGuard.Inspect(wire, McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(null, McpTransportGuardTestData.Target)));
        await Assert.That(wire.AsSpan().SequenceEqual(snapshot)).IsTrue();
    }

    /// <summary>Wrong type or mismatched transport fields use fixed safe Validation before native parsing.</summary>
    /// <param name="fault">The concrete body mutation.</param>
    [Test]
    [Arguments(McpTransportFrameFault.MethodMismatch)]
    [Arguments(McpTransportFrameFault.MethodWrongType)]
    [Arguments(McpTransportFrameFault.NameMismatch)]
    [Arguments(McpTransportFrameFault.NameWrongType)]
    [Arguments(McpTransportFrameFault.VersionMismatch)]
    [Arguments(McpTransportFrameFault.VersionWrongType)]
    [Arguments(McpTransportFrameFault.VersionNull)]
    [Arguments(McpTransportFrameFault.MetaMismatch)]
    [Arguments(McpTransportFrameFault.MetaWrongType)]
    [Arguments(McpTransportFrameFault.MetaNull)]
    public async Task InvalidFrameFieldUsesFixedSafeValidation(McpTransportFrameFault fault)
    {
        var wire = McpTransportGuardTestData.InvalidFrame(fault);
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers());
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpTransportGuard.Inspect(wire, headers));
        var baseline = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.InvalidHeaders(McpTransportHeaderFault.MarkerRevision)));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(baseline.Message);
        await Assert.That(error.Message).DoesNotContain(McpTransportGuardTestData.Marker);
    }

    /// <summary>Both native version locations match the pinned revision without altering the serialized payload.</summary>
    [Test]
    public async Task MatchingProtocolFieldsPreserveExactWireBytes()
    {
        var wire = McpTransportGuardTestData.Frame(RequestMethods.ToolsCall, new()
        {
            [McpTransportGuardTestData.NameField] = McpTransportGuardTestData.Target,
            [McpTransportGuardTestData.VersionField] = McpTransportGuardTestData.Revision,
            [McpTransportGuardTestData.MetaField] = new Dictionary<string, object?> { [MetaKeys.ProtocolVersion] = McpTransportGuardTestData.Revision }
        });
        var snapshot = wire.ToArray();
        McpTransportGuard.Inspect(wire, McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers()));
        await Assert.That(wire.AsSpan().SequenceEqual(snapshot)).IsTrue();
    }

    /// <summary>Missing optional routing headers and protocol metadata are left for the native SDK.</summary>
    [Test]
    public async Task AbsentOptionalTransportFieldsAreNotInvented()
    {
        var wire = McpTransportGuardTestData.Frame(RequestMethods.ToolsCall, new());
        var snapshot = wire.ToArray();
        McpTransportGuard.Inspect(wire, McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(null, null)));
        await Assert.That(wire.AsSpan().SequenceEqual(snapshot)).IsTrue();
    }

    /// <summary>Business method and protocol-like values remain untouched by the transport comparison.</summary>
    [Test]
    public async Task BusinessValuesRemainUntouched()
    {
        var request = new Dictionary<string, object?>
        {
            [McpTransportGuardTestData.MethodField] = McpTransportGuardTestData.Marker,
            [McpTransportGuardTestData.VersionField] = McpTransportGuardTestData.Marker,
            [MetaKeys.ProtocolVersion] = McpTransportGuardTestData.Marker
        };
        var wire = McpTransportGuardTestData.Frame(RequestMethods.ToolsCall, new()
        {
            [McpTransportGuardTestData.NameField] = McpTransportGuardTestData.Target,
            [McpTransportGuardTestData.ArgumentsField] = new Dictionary<string, object?> { [McpTransportGuardTestData.RequestField] = request }
        });
        var snapshot = wire.ToArray();
        McpTransportGuard.Inspect(wire, McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers()));
        await Assert.That(wire.AsSpan().SequenceEqual(snapshot)).IsTrue();
    }
}
