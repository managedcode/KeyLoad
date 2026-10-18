using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class ModelRootClusterResults
{
    private const long EmptyStorageCut = 0;
    internal static async Task PageAsync(QueryPage page, ModelRootClusterState state, bool read, bool explicitValue)
    {
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(state.AccessPath);
        await Assert.That(page.CutPosition).IsGreaterThan(EmptyStorageCut);
        await Assert.That(page.Rows.Length).IsEqualTo(1);
        var json = JsonNode.Parse(state.Original.Json)!.AsObject();
        var literalComponent = state.HeaderRoot ? ModelRootClusterState.PayloadField : ModelRootClusterState.HeadersField;
        json[literalComponent]!.AsObject().Remove("");
        if (!read)
        {
            json.Remove(state.Component);
        }
        var expectedJson = explicitValue ? new JsonObject { [ModelRootClusterState.SecretField] = read ? "ROOT-CANARY" : null } : json;
        ImmutableArray<string> omitted = read ? ["/" + literalComponent + "/"]
            : state.HeaderRoot ? ["/payload/", "/headers"] : ["/payload", "/headers/"];
        if (explicitValue)
        {
            omitted = read ? [] : [state.HeaderRoot ? "/headers" : "/payload"];
        }
        var expected = new QueryRow(ModelRootClusterState.Item, 1L, expectedJson.ToJsonString(), !omitted.IsEmpty, omitted);
        var actual = page.Rows.Single();
        using var actualJson = JsonDocument.Parse(actual.Json);
        using var goldenJson = JsonDocument.Parse(expected.Json);
        await Assert.That(JsonElement.DeepEquals(actualJson.RootElement, goldenJson.RootElement)).IsTrue();
        await SqlClusterProtocol.EqualAsync(expected with
        {
            Json = ""
        }, actual with
        {
            Json = ""
        });
    }
}
