using System.Text.Json;
using KeyLoad.Server;
using ModelContextProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCPGW-001/002: the official list is the exact fixed meta-tool set.</summary>
internal sealed class McpGatewayProtocolTests
{
    private const string SearchName = "gateway_tools_search";
    private const string RouteName = "gateway_tools_route";
    private const string InvokeName = "gateway_tool_invoke";
    private const int MaximumListBytes = 65_536;

    [Test]
    public async Task ListContainsOnlyTheThreeStrictMetaTools()
    {
        var page = McpGatewayToolPagination.Create(null, MaximumListBytes);
        string[] names = [SearchName, RouteName, InvokeName];
        await Assert.That(page.Tools.Select(tool => tool.Name).SequenceEqual(names)).IsTrue();
        await Assert.That(page.NextCursor).IsNull();
        await Assert.That(page.Tools.All(tool => tool.Annotations is not null)).IsTrue();
        await Assert.That(page.Tools.Where(tool => tool.Name != InvokeName).All(tool => tool.Annotations!.ReadOnlyHint)).IsTrue();
        var invoke = page.Tools.Single(tool => tool.Name == InvokeName).Annotations!;
        await Assert.That(invoke.ReadOnlyHint).IsFalse();
        await Assert.That(invoke.IdempotentHint).IsFalse();
        await Assert.That(invoke.DestructiveHint).IsTrue();
        foreach (var tool in page.Tools)
        {
            await Assert.That(tool.InputSchema.GetProperty("additionalProperties").ValueKind).IsEqualTo(JsonValueKind.False);
            var output = tool.OutputSchema!.Value;
            var success = output.GetProperty("oneOf")[0];
            var failure = output.GetProperty("oneOf")[1];
            await Assert.That(success.GetProperty("properties").GetProperty("requestId").GetProperty("type").ValueKind)
                .IsEqualTo(JsonValueKind.Array);
            await Assert.That(failure.GetProperty("properties").GetProperty("error").ValueKind).IsEqualTo(JsonValueKind.Object);
        }
        var searchMatches = page.Tools.Single(tool => tool.Name == SearchName).OutputSchema!.Value
            .GetProperty("oneOf")[0].GetProperty("properties").GetProperty("result").GetProperty("properties")
            .GetProperty("matches");
        await Assert.That(searchMatches.GetProperty("type").GetString()).IsEqualTo("array");
        await Assert.That(searchMatches.GetProperty("items").GetProperty("required").GetArrayLength()).IsEqualTo(2);
        var routeCategories = page.Tools.Single(tool => tool.Name == RouteName).OutputSchema!.Value
            .GetProperty("oneOf")[0].GetProperty("properties").GetProperty("result").GetProperty("properties")
            .GetProperty("categories");
        await Assert.That(routeCategories.GetProperty("type").GetString()).IsEqualTo("array");
        await Assert.That(routeCategories.GetProperty("items").GetProperty("required").GetArrayLength()).IsEqualTo(3);
        var search = page.Tools.Single(tool => tool.Name == SearchName).Annotations!;
        var route = page.Tools.Single(tool => tool.Name == RouteName).Annotations!;
        await Assert.That(search.ReadOnlyHint && search.IdempotentHint && !search.DestructiveHint).IsTrue();
        await Assert.That(route.ReadOnlyHint && route.IdempotentHint && !route.DestructiveHint).IsTrue();
    }

    [Test]
    public async Task NonemptyCursorAndUnfittableListFailClosed()
    {
        var cursor = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayToolPagination.Create("x", MaximumListBytes));
        await Assert.That(cursor.Code).IsEqualTo(ErrorCode.Validation);
        var exact = NativeListBytes();
        var tooSmall = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayToolPagination.Create(null, exact - 1));
        await Assert.That(tooSmall.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    [Test]
    public async Task QueryAndRouteArgumentsUseUtf8AndFiniteCountBounds()
    {
        var exactQuery = new string('é', 1024);
        var accepted = McpGatewayMetaArgumentReader.Read(McpGatewayMetaOperation.Search,
            McpGatewayProtocolInputs.Read(JsonSerializer.Serialize(new { query = exactQuery })));
        await Assert.That(accepted.SearchLimit).IsEqualTo(3);
        var tooLong = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaArgumentReader.Read(
            McpGatewayMetaOperation.Search, McpGatewayProtocolInputs.Read(JsonSerializer.Serialize(new { query = exactQuery + "é" }))));
        await Assert.That(tooLong.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(accepted.Query).IsEqualTo(exactQuery);
    }

    [Test]
    public async Task RouteDefaultsAndExplicitLimitsStayWithinFourResults()
    {
        var route = McpGatewayMetaArgumentReader.Read(McpGatewayMetaOperation.Route,
            McpGatewayProtocolInputs.Read("{\"query\":\"graph task\",\"maxCategories\":2,\"maxToolsPerCategory\":2,\"preferReadOnly\":false}"));
        await Assert.That(route.CategoryLimit).IsEqualTo(2);
        await Assert.That(route.ToolsPerCategory).IsEqualTo(2);
        await Assert.That(route.PreferReadOnly).IsFalse();
        await Assert.That(route.Query).IsEqualTo("graph task");
        var invalid = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaArgumentReader.Read(
            McpGatewayMetaOperation.Route, McpGatewayProtocolInputs.Read("{\"query\":\"x\",\"maxCategories\":3}")));
        await Assert.That(invalid.Code).IsEqualTo(ErrorCode.Validation);
    }


    [Test]
    public async Task MetaArgumentsRejectUnknownCaseChangedAndDuplicateNestedFields()
    {
        var unknown = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaArgumentReader.Read(
            McpGatewayMetaOperation.Search, McpGatewayProtocolInputs.Read("{\"query\":\"read\",\"extra\":1}")));
        var changed = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaArgumentReader.Read(
            McpGatewayMetaOperation.Search, McpGatewayProtocolInputs.Read("{\"Query\":\"read\"}")));
        var blankTarget = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaArgumentReader.Read(
            McpGatewayMetaOperation.Invoke, McpGatewayProtocolInputs.Read("{\"toolId\":\" \"}")));
        var duplicate = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaArgumentReader.Read(
            McpGatewayMetaOperation.Invoke, McpGatewayProtocolInputs.Read("{\"toolId\":\"keyload_documents_get\",\"arguments\":{\"x\":1,\"x\":2}}")));
        await Assert.That(unknown.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(changed.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(blankTarget.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(duplicate.Code).IsEqualTo(ErrorCode.Validation);
    }

    private static int NativeListBytes()
    {
        var page = McpGatewayToolPagination.Create(null, MaximumListBytes);
        return JsonSerializer.SerializeToUtf8Bytes(page, McpJsonUtilities.DefaultOptions).Length;
    }
}
