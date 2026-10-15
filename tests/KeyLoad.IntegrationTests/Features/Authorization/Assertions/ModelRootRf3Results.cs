using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class ModelRootRf3Results
{
    private const long EmptyStorageCut = 0;
    internal static async Task PageAsync(QueryPage page, ModelRootRf3State state, bool read, bool explicitValue)
    {
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(state.AccessPath);
        await Assert.That(page.CutPosition).IsGreaterThan(EmptyStorageCut);
        await Assert.That(page.Rows.Length).IsEqualTo(1);
        var json = JsonNode.Parse(state.Original.Json)!.AsObject();
        var literalComponent = state.HeaderRoot ? ModelRootRf3State.PayloadField : ModelRootRf3State.HeadersField;
        json[literalComponent]!.AsObject().Remove("");
        if (!read)
        {
            json.Remove(state.Component);
        }
        var expectedJson = explicitValue ? new JsonObject { [ModelRootRf3State.SecretField] = read ? "ROOT-CANARY" : null } : json;
        ImmutableArray<string> omitted = read ? ["/" + literalComponent + "/"]
            : state.HeaderRoot ? ["/payload/", "/headers"] : ["/payload", "/headers/"];
        var expected = new QueryRow(ModelRootRf3State.Item, 1L, expectedJson.ToJsonString(), true, omitted);
        var actual = page.Rows.Single();
        using var actualJson = JsonDocument.Parse(actual.Json);
        using var goldenJson = JsonDocument.Parse(expected.Json);
        await Assert.That(JsonElement.DeepEquals(actualJson.RootElement, goldenJson.RootElement)).IsTrue();
        await SqlRf3Protocol.EqualAsync(expected with
        {
            Json = ""
        }, actual with
        {
            Json = ""
        });
    }
}
