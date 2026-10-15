using System.Reflection;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using P = KeyLoad.IntegrationTests.Features.QueryExecution.ScriptDomNamedParameterRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ScriptDomNamedParameterRf3Writes
{
    internal static Dictionary<string, JsonElement> Parameters()
        => new(StringComparer.Ordinal)
        {
            [P.Id] = JsonSerializer.SerializeToElement(P.TypedId),
            [P.Title] = JsonSerializer.SerializeToElement(P.TypedTitle),
            [P.Amount] = JsonSerializer.SerializeToElement(P.TypedAmount)
        };
    internal static async Task RunAsync(RequestCqrsRf3Callers root, SqlParserAdmissionRf3State state, CancellationToken token)
    {
        var mismatched = Parameters().ToDictionary(entry => entry.Key == P.Amount ? P.Amount.ToUpperInvariant() : entry.Key,
            entry => entry.Value, StringComparer.Ordinal);
        var refused = state.Insert with
        {
            Parameters = mismatched
        };
        await SqlParserAdmissionRf3Current.ObserveAsync(root, state, async () =>
        {
            await ScriptDomNamedParameterRf3Refusals.UnifiedAsync(await root.Sdk.ExecuteSqlAsync(refused, token), P.InvalidOperation);
            await ReceiptAsync(state.InsertReceipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(root.Sdk, state.Insert, token));
            await ScriptDomNamedParameterRf3Refusals.McpAsync(await root.Mcp.CallAsync(
                SqlOperationProtocol.ToolName, refused, token), state.RootCredential, dispatched: false);
            await ReceiptAsync(state.InsertReceipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(root.Mcp, state.Insert, token));
        }, token);
        await SqlParserAdmissionRf3Current.CheckAsync(root, state, token);
    }
    private static async Task ReceiptAsync(CommitReceipt original, CommitReceipt actual)
    {
        await SqlRf3Protocol.EqualAsync(original, actual);
        foreach (var mutation in actual.Mutations)
        {
            var references = typeof(MutationReceipt).GetProperty(P.CompositionProperty,
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mutation)
                ?? throw new InvalidOperationException();
            await Assert.That(references.GetType().GetProperty(P.EmptyProperty)?.GetValue(references) is true).IsTrue();
        }
    }
}
