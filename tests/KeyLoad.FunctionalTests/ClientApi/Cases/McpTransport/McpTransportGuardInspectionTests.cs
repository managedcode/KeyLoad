using KeyLoad.Server;
using ModelContextProtocol.Protocol;

namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>AC-MCP-002/003/004: actual framing-checked UTF-8 is matched without inspecting business authority.</summary>
internal sealed class McpTransportGuardInspectionTests
{
    /// <summary>A present target is selected by the body method even when the HTTP method header is absent.</summary>
    /// <param name="method">The execution method that selects name or URI.</param>
    /// <param name="field">The execution target parameter key.</param>
    /// <param name="target">The exact target value.</param>
    [Test]
    [Arguments(RequestMethods.ToolsCall, McpTransportGuardTestData.NameField, McpTransportGuardTestData.Target)]
    [Arguments(RequestMethods.PromptsGet, McpTransportGuardTestData.NameField, McpTransportGuardTestData.Target)]
    [Arguments(RequestMethods.ResourcesRead, McpTransportGuardTestData.UriField, McpTransportGuardTestData.Resource)]
    public async Task BodyMethodSelectsPresentNameHeader(string method, string field, string target)
    {
        var wire = McpTransportGuardTestData.Frame(method, new()
        {
            [field] = target
        });
        var snapshot = wire.ToArray();
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(null, target), UnitMcpOptions.Execution());
        McpTransportGuard.Inspect(wire, headers);
        await Assert.That(wire.AsSpan().SequenceEqual(snapshot)).IsTrue();
        await Assert.That(McpTransportGuardTestData.ReadTarget(wire, field)).IsEqualTo(target);
    }

    /// <summary>A mismatched target is rejected according to the body-selected field even without a method header.</summary>
    /// <param name="method">The execution body method that selects the target field.</param>
    /// <param name="field">The execution target parameter key.</param>
    [Test]
    [Arguments(RequestMethods.ToolsCall, McpTransportGuardTestData.NameField)]
    [Arguments(RequestMethods.PromptsGet, McpTransportGuardTestData.NameField)]
    [Arguments(RequestMethods.ResourcesRead, McpTransportGuardTestData.UriField)]
    public async Task BodySelectedTargetMismatchUsesSafeValidation(string method, string field)
    {
        var wire = McpTransportGuardTestData.Frame(method, new()
        {
            [field] = McpTransportGuardTestData.Marker
        });
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(null, McpTransportGuardTestData.Target), UnitMcpOptions.Execution());
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpTransportGuard.Inspect(wire, headers));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).DoesNotContain(McpTransportGuardTestData.Marker);
    }

    /// <summary>Absent or unrouted body methods do not invent target routing ahead of execution protocol validation.</summary>
    /// <param name="missingMethod">Whether to omit the body method rather than use a method without target routing.</param>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task UnroutedBodyMethodLeavesValidationIntact(bool missingMethod)
    {
        var method = missingMethod ? null : RequestMethods.ServerDiscover;
        var wire = McpTransportGuardTestData.Frame(method, new()
        {
            [McpTransportGuardTestData.NameField] = McpTransportGuardTestData.Marker
        });
        var snapshot = wire.ToArray();
        McpTransportGuard.Inspect(wire, McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(null, McpTransportGuardTestData.Target), UnitMcpOptions.Execution()));
        await Assert.That(wire.AsSpan().SequenceEqual(snapshot)).IsTrue();
    }

    /// <summary>Wrong type or mismatched transport fields use fixed safe Validation before execution parsing.</summary>
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
        var headers = McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(), UnitMcpOptions.Execution());
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpTransportGuard.Inspect(wire, headers));
        var baseline = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpTransportGuard.ReadHeaders(McpTransportGuardTestData.InvalidHeaders(McpTransportHeaderFault.MarkerRevision), UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(baseline.Message);
        await Assert.That(error.Message).DoesNotContain(McpTransportGuardTestData.Marker);
    }

    /// <summary>Both execution version locations match the pinned revision without altering the serialized payload.</summary>
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
        McpTransportGuard.Inspect(wire, McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(), UnitMcpOptions.Execution()));
        await Assert.That(wire.AsSpan().SequenceEqual(snapshot)).IsTrue();
    }

    /// <summary>Missing optional routing headers and protocol metadata are left for the execution SDK.</summary>
    [Test]
    public async Task AbsentOptionalTransportFieldsAreNotInvented()
    {
        var wire = McpTransportGuardTestData.Frame(RequestMethods.ToolsCall, new());
        var snapshot = wire.ToArray();
        McpTransportGuard.Inspect(wire, McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(null, null), UnitMcpOptions.Execution()));
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
        McpTransportGuard.Inspect(wire, McpTransportGuard.ReadHeaders(McpTransportGuardTestData.Headers(), UnitMcpOptions.Execution()));
        await Assert.That(wire.AsSpan().SequenceEqual(snapshot)).IsTrue();
    }
}
