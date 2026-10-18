using System.Reflection;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using P = KeyLoad.IntegrationTests.Features.QueryExecution.ScriptDomNamedParameterClusterProtocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ScriptDomNamedParameterClusterWrites
{
    internal static Dictionary<string, JsonElement> Parameters()
        => new(StringComparer.Ordinal)
        {
            [P.Id] = JsonSerializer.SerializeToElement(P.TypedId),
            [P.Title] = JsonSerializer.SerializeToElement(P.TypedTitle),
            [P.Amount] = JsonSerializer.SerializeToElement(P.TypedAmount)
        };
    internal static async Task RunAsync(RequestCqrsClusterCallers root, SqlParserAdmissionClusterState state, CancellationToken token)
    {
        var mismatched = Parameters().ToDictionary(entry => entry.Key == P.Amount ? P.Amount.ToUpperInvariant() : entry.Key,
            entry => entry.Value, StringComparer.Ordinal);
        var refused = state.Insert with
        {
            Parameters = mismatched
        };
        await SqlParserAdmissionClusterCurrent.ObserveAsync(root, state, async () =>
        {
            await ScriptDomNamedParameterClusterRefusals.UnifiedAsync(await root.Sdk.ExecuteSqlAsync(refused, token), P.InvalidOperation);
            await ReceiptAsync(state.InsertReceipt, await SqlClusterProtocol.SdkAsync<CommitReceipt>(root.Sdk, state.Insert, token));
            await ScriptDomNamedParameterClusterRefusals.McpAsync(await root.Mcp.CallAsync(
                SqlOperationProtocol.ToolName, refused, token), state.RootCredential, dispatched: false);
            await ReceiptAsync(state.InsertReceipt, await SqlClusterProtocol.McpAsync<CommitReceipt>(root.Mcp, state.Insert, token));
        }, token);
        await SqlParserAdmissionClusterCurrent.CheckAsync(root, state, token);
    }
    private static async Task ReceiptAsync(CommitReceipt original, CommitReceipt actual)
    {
        await SqlClusterProtocol.EqualAsync(original, actual);
        foreach (var mutation in actual.Mutations)
        {
            var references = typeof(MutationReceipt).GetProperty(P.CompositionProperty,
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mutation)
                ?? throw new InvalidOperationException();
            await Assert.That(references.GetType().GetProperty(P.EmptyProperty)?.GetValue(references) is true).IsTrue();
        }
    }
}
