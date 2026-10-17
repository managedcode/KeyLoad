using System.Text.Json.Nodes;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCPGW-003/004: exact inner targets are selected before typed decoding.</summary>
internal sealed class McpGatewayAdmissionTests
{
    private const string InvokeName = "gateway_tool_invoke";
    private const string DocumentsGet = "keyload_documents_get";
    private const string Marker = "unknown-caller-target";
    private const string CaseChangedTarget = "Keyload_documents_get";

    [Test]
    public async Task InvocationSelectsTheExactCanonicalRouteBeforeDecode()
    {
        var arguments = JsonNode.Parse("{\"toolId\":\"keyload_documents_get\",\"arguments\":{\"request\":{}}}")!;
        var selection = McpGatewayMetaArgumentReader.Select(InvokeName, arguments);
        await Assert.That(selection.Operation).IsEqualTo(McpGatewayMetaOperation.Invoke);
        await Assert.That(selection.CanonicalOperation!.Name).IsEqualTo(DocumentsGet);
        await Assert.That(selection.CanonicalOperation.Route).IsEqualTo(McpToolRoutes.DocumentsGet);
    }

    [Test]
    public async Task DirectOldToolCallsAndUnknownOrRecursiveTargetsAreRejected()
    {
        await AssertUnsupportedAsync(DocumentsGet, null);
        await AssertUnsupportedAsync(InvokeName, JsonNode.Parse("{\"toolId\":\"" + Marker + "\"}"));
        await AssertUnsupportedAsync(InvokeName, JsonNode.Parse("{\"toolId\":\"" + CaseChangedTarget + "\"}"));
        await AssertUnsupportedAsync(InvokeName, JsonNode.Parse("{\"toolId\":\"gateway_tools_search\"}"));
        await Assert.That(McpOperationCatalog.Entries.Any(entry => entry.Name == InvokeName)).IsFalse();
    }

    [Test]
    public async Task SearchAndRouteSelectionUseOnlyTheControlLaneDescriptor()
    {
        var search = McpGatewayMetaArgumentReader.Select("gateway_tools_search", JsonNode.Parse("{}"));
        var route = McpGatewayMetaArgumentReader.Select("gateway_tools_route", JsonNode.Parse("{}"));
        await Assert.That(search.CanonicalOperation).IsNull();
        await Assert.That(route.CanonicalOperation).IsNull();
        await Assert.That(search.Operation).IsEqualTo(McpGatewayMetaOperation.Search);
        await Assert.That(route.Operation).IsEqualTo(McpGatewayMetaOperation.Route);
    }

    private static async Task AssertUnsupportedAsync(string? name, JsonNode? arguments)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaArgumentReader.Select(name, arguments));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
    }
}
