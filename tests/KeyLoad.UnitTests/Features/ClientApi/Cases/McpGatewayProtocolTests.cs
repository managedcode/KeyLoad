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
    private const string AdditionalPropertiesKey = "additionalProperties";
    private const string OneOfKey = "oneOf";
    private const string PropertiesKey = "properties";
    private const string RequestIdKey = "requestId";
    private const string TypeKey = "type";
    private const string ErrorKey = "error";
    private const string ResultKey = "result";
    private const string MatchesKey = "matches";
    private const string ItemsKey = "items";
    private const string RequiredKey = "required";
    private const string CategoriesKey = "categories";
    private const int MaximumListBytes = 65_536;

    [Test]
    public async Task ListContainsOnlyTheThreeStrictMetaTools()
    {
        var page = McpGatewayToolPagination.Create(null, MaximumListBytes);
        string[] names = [SearchName, RouteName, InvokeName];
        await Assert.That(page.Tools.Select(tool => tool.Name).SequenceEqual(names)).IsTrue();
        await Assert.That(page.NextCursor).IsNull();
        await Assert.That(page.Tools.All(tool => tool.Annotations is not null)).IsTrue();
        await Assert.That(page.Tools.Where(tool => tool.Name != InvokeName).All(tool => tool.Annotations!.ReadOnlyHint == true)).IsTrue();
        var invoke = page.Tools.Single(tool => tool.Name == InvokeName).Annotations!;
        await Assert.That(invoke.ReadOnlyHint).IsFalse();
        await Assert.That(invoke.IdempotentHint).IsFalse();
        await Assert.That(invoke.DestructiveHint).IsTrue();
        foreach (var tool in page.Tools)
        {
            await Assert.That(tool.InputSchema.GetProperty(AdditionalPropertiesKey).ValueKind).IsEqualTo(JsonValueKind.False);
            var output = tool.OutputSchema!.Value;
            var success = output.GetProperty(OneOfKey)[0];
            var failure = output.GetProperty(OneOfKey)[1];
            await Assert.That(success.GetProperty(PropertiesKey).GetProperty(RequestIdKey).GetProperty(TypeKey).ValueKind)
                .IsEqualTo(JsonValueKind.Array);
            await Assert.That(failure.GetProperty(PropertiesKey).GetProperty(ErrorKey).ValueKind).IsEqualTo(JsonValueKind.Object);
        }
        var searchMatches = page.Tools.Single(tool => tool.Name == SearchName).OutputSchema!.Value
            .GetProperty(OneOfKey)[0].GetProperty(PropertiesKey).GetProperty(ResultKey).GetProperty(PropertiesKey)
            .GetProperty(MatchesKey);
        await Assert.That(searchMatches.GetProperty(TypeKey).GetString()).IsEqualTo("array");
        await Assert.That(searchMatches.GetProperty(ItemsKey).GetProperty(RequiredKey).GetArrayLength()).IsEqualTo(2);
        var routeCategories = page.Tools.Single(tool => tool.Name == RouteName).OutputSchema!.Value
            .GetProperty(OneOfKey)[0].GetProperty(PropertiesKey).GetProperty(ResultKey).GetProperty(PropertiesKey)
            .GetProperty(CategoriesKey);
        await Assert.That(routeCategories.GetProperty(TypeKey).GetString()).IsEqualTo("array");
        await Assert.That(routeCategories.GetProperty(ItemsKey).GetProperty(RequiredKey).GetArrayLength()).IsEqualTo(3);
        var search = page.Tools.Single(tool => tool.Name == SearchName).Annotations!;
        var route = page.Tools.Single(tool => tool.Name == RouteName).Annotations!;
        await Assert.That(search.ReadOnlyHint == true && search.IdempotentHint == true && search.DestructiveHint == false).IsTrue();
        await Assert.That(route.ReadOnlyHint == true && route.IdempotentHint == true && route.DestructiveHint == false).IsTrue();
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
            McpGatewayProtocolInputs.Read(JsonSerializer.Serialize(new { query = exactQuery })), UnitMcpOptions.Execution());
        await Assert.That(accepted.SearchLimit).IsEqualTo(3);
        var tooLong = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaArgumentReader.Read(
            McpGatewayMetaOperation.Search, McpGatewayProtocolInputs.Read(JsonSerializer.Serialize(new { query = exactQuery + "é" })), UnitMcpOptions.Execution()));
        await Assert.That(tooLong.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(accepted.Query).IsEqualTo(exactQuery);
    }

    [Test]
    public async Task RouteDefaultsAndExplicitLimitsStayWithinFourResults()
    {
        var route = McpGatewayMetaArgumentReader.Read(McpGatewayMetaOperation.Route,
            McpGatewayProtocolInputs.Read("{\"query\":\"graph task\",\"maxCategories\":2,\"maxToolsPerCategory\":2,\"preferReadOnly\":false}"), UnitMcpOptions.Execution());
        await Assert.That(route.CategoryLimit).IsEqualTo(2);
        await Assert.That(route.ToolsPerCategory).IsEqualTo(2);
        await Assert.That(route.PreferReadOnly).IsFalse();
        await Assert.That(route.Query).IsEqualTo("graph task");
        var invalid = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaArgumentReader.Read(
            McpGatewayMetaOperation.Route, McpGatewayProtocolInputs.Read("{\"query\":\"x\",\"maxCategories\":3}"), UnitMcpOptions.Execution()));
        await Assert.That(invalid.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task MetaArgumentsRejectUnknownCaseChangedAndDuplicateNestedFields()
    {
        var unknown = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaArgumentReader.Read(
            McpGatewayMetaOperation.Search, McpGatewayProtocolInputs.Read("{\"query\":\"read\",\"extra\":1}"), UnitMcpOptions.Execution()));
        var changed = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaArgumentReader.Read(
            McpGatewayMetaOperation.Search, McpGatewayProtocolInputs.Read("{\"Query\":\"read\"}"), UnitMcpOptions.Execution()));
        var blankTarget = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaArgumentReader.Read(
            McpGatewayMetaOperation.Invoke, McpGatewayProtocolInputs.Read("{\"toolId\":\" \"}"), UnitMcpOptions.Execution()));
        var duplicate = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaArgumentReader.Read(
            McpGatewayMetaOperation.Invoke, McpGatewayProtocolInputs.Read("{\"toolId\":\"keyload_documents_get\",\"arguments\":{\"x\":1,\"x\":2}}"), UnitMcpOptions.Execution()));
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
