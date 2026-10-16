using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.RelationalStorage.DropTableLifecycleSeedProtocol;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleSeedAssertions
{
    internal static async Task BeforeAsync(RequestCqrsRf3Callers callers,
        DropTableLifecycleSeedState state, CancellationToken token)
    {
        await RowsAsync(callers, state.Tables.SelectMany(table => table.Rows).ToImmutableArray(), token);
        await ControlsAsync(callers, state, token);
        var request = new TraverseRequest(state.Partition, P.Graph, state.Anchor.Reference,
            P.MaxDepth, P.VertexCount, P.TableCount);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.TraverseAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(await callers.Mcp.CallAsync(
            McpCallerTools.GraphTraverse, request, token))).Value;
        await GraphAsync(state, sdk);
        await GraphAsync(state, official);
    }

    internal static async Task AfterAsync(RequestCqrsRf3Callers callers,
        DropTableLifecycleSeedState state, CancellationToken token)
    {
        await ControlsAsync(callers, state, token);
        var request = new TraverseRequest(state.Partition, P.Graph, state.Anchor.Reference,
            P.MaxDepth, P.VertexCount, P.TableCount);
        var expected = new global::KeyLoad.GraphTraversal([state.Anchor.Reference], []);
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.TraverseAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected,
            (await McpCallerAssertions.SuccessAsync<global::KeyLoad.GraphTraversal>(await callers.Mcp.CallAsync(
                McpCallerTools.GraphTraverse, request, token))).Value);
    }

    internal static Task ControlsAsync(RequestCqrsRf3Callers callers,
        DropTableLifecycleSeedState state, CancellationToken token)
        => RowsAsync(callers, [state.Anchor, state.ControlRow], token);

    internal static async Task RowsAsync(RequestCqrsRf3Callers callers,
        ImmutableArray<DocumentResult> rows, CancellationToken token)
    {
        foreach (var row in rows)
        {
            var sdk = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(row.Reference, token));
            var mcp = await PublicReadAssertions.DocumentAsync(await callers.Mcp.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(row.Reference), token));
            await SqlRf3Protocol.EqualAsync<DocumentResult?>(row, sdk);
            await SqlRf3Protocol.EqualAsync<DocumentResult?>(row, mcp.Value);
        }
    }

    internal static async Task ReceiptAsync(CommandRequest command, CommitReceipt actual,
        AtomicPartitionPlacementResolution placement)
    {
        await Assert.That(actual.Token.Position).IsGreaterThan(P.Absent);
        var expected = new CommitReceipt(command.CommandId, new(placement.Incarnation,
            command.Partition.AtomicPartitionId, actual.Token.Position, placement.PlacementEpoch),
            DropTableLifecycleSeedMutations.Expected, DurabilityProfile.QuorumProcessDurable);
        await NativeSubmissionColdAssertions.ReceiptAsync(actual, expected);
    }

    private static async Task GraphAsync(DropTableLifecycleSeedState state, global::KeyLoad.GraphTraversal actual)
    {
        var vertices = state.Tables.Select(table => table.Rows[P.First].Reference).Append(state.Anchor.Reference).ToArray();
        await Assert.That(actual.Vertices.Length).IsEqualTo(P.VertexCount);
        await Assert.That(actual.Vertices.ToHashSet().SetEquals(vertices)).IsTrue();
        await Assert.That(actual.Edges.Length).IsEqualTo(P.TableCount);
        for (var table = P.First; table < P.TableCount; table++)
        {
            var expected = new EdgeRecord(P.Edges[table], state.Anchor.Reference,
                state.Tables[table].Rows[P.First].Reference, P.EdgeLabel, P.EmptyJson, P.Initial);
            await SqlRf3Protocol.EqualAsync(expected, actual.Edges.Single(edge => edge.Id == expected.Id));
        }
    }
}
