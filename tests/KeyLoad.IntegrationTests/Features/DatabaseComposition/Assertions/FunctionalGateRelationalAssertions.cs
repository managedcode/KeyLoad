using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateRelationalProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateRelationalAssertions
{
    internal static async Task ReceiptAsync(CommitReceipt actual, Guid id, PartitionRef partition,
        ImmutableArray<MutationReceipt> expected)
    {
        await Assert.That(actual.CommandId).IsEqualTo(id);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(actual.Token.Incarnation == Guid.Empty).IsFalse();
        await Assert.That(actual.Token.Position).IsGreaterThan(P.Absent);
        await Assert.That(actual.Token.OwnershipEpoch).IsGreaterThan(P.Absent);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, actual.Mutations);
    }

    internal static async Task PageAsync(QueryPage actual, ImmutableArray<QueryRow> rows)
    {
        await SqlRf3Protocol.EqualAsync(rows, actual.Rows);
        await Assert.That(actual.Cursor).IsNull();
        await Assert.That(actual.CutPosition).IsGreaterThan(P.Absent);
    }

    internal static async Task ParentAsync(FunctionalGateContext ctx, string id, long revision,
        string json, CancellationToken token)
    {
        var query = FunctionalGateRelationalCalls.Query(ctx, P.ParentSelection, id);
        ImmutableArray<QueryRow> expected = [new(id, revision, json, false, [])];
        await PageAsync(await FunctionalGateRelationalCalls.SqlSdkAsync<QueryPage>(ctx, ctx.Reader, query, token), expected);
        await PageAsync(await FunctionalGateRelationalCalls.SqlMcpAsync<QueryPage>(ctx, ctx.Reader, query, token), expected);
    }

    internal static async Task JoinAsync(FunctionalGateContext ctx, string id, bool linked, CancellationToken token)
    {
        var query = FunctionalGateRelationalCalls.Query(ctx, P.Join, id);
        ImmutableArray<QueryRow> expected = linked
            ? [new(id, P.Updated, FunctionalGateRelationalRows.JoinJson(id), false, [],
                [new(P.LeftAlias, id, P.Updated), new(P.RightAlias, id, P.Updated)])] : [];
        var sdk = await FunctionalGateRelationalCalls.SqlSdkAsync<QueryPage>(ctx, ctx.Reader, query, token);
        var mcp = await FunctionalGateRelationalCalls.SqlMcpAsync<QueryPage>(ctx, ctx.Reader, query, token);
        await PageAsync(sdk, expected);
        await PageAsync(mcp, expected);
        await Assert.That(sdk.AccessPath).IsEqualTo(P.JoinAccessPath);
        await Assert.That(mcp.AccessPath).IsEqualTo(P.JoinAccessPath);
    }

    internal static async Task AnchorAsync(FunctionalGateContext ctx, string id, CommitToken minimum, CancellationToken token)
    {
        var actual = await ctx.SdkAsync(ctx.Reader.Sdk.GetAsync(new(ctx.Partition, P.Parent, id), minimum, token));
        await Assert.That(actual.Document).IsNotNull();
        await Assert.That(actual.Document!.Reference).IsEqualTo(new EntityRef(ctx.Partition, P.Parent, id));
        await Assert.That(actual.Document.Revision).IsEqualTo(P.Initial);
        await Assert.That(actual.Document.Redacted).IsFalse();
        await Assert.That(actual.Document.RedactedFields.IsEmpty).IsTrue();
        using var json = JsonDocument.Parse(actual.Document.Json);
        await Assert.That(json.RootElement.GetProperty(P.DocumentId).GetString()).IsEqualTo(ctx.Anchor.Id);
        await Assert.That(json.RootElement.GetProperty(P.AnchorReference).GetString())
            .IsEqualTo(JsonSerializer.Serialize(ctx.Anchor, JsonDefaults.Options));
    }

    internal static async Task BaselineAsync(FunctionalGateContext ctx, CancellationToken token)
    {
        await ParentAsync(ctx, P.Baseline, P.Initial, P.BaselineParent, token);
        var child = FunctionalGateRelationalCalls.Query(ctx, P.ChildSelection, P.Baseline);
        await PageAsync(await FunctionalGateRelationalCalls.SqlMcpAsync<QueryPage>(ctx, ctx.Reader, child, token),
            [new(P.Baseline, P.Initial, P.BaselineChild, false, [])]);
        var independent = FunctionalGateRelationalCalls.Query(ctx, P.IndependentSelection, P.Baseline);
        await PageAsync(await FunctionalGateRelationalCalls.SqlSdkAsync<QueryPage>(ctx, ctx.Reader, independent, token),
            [new(P.Baseline, P.Initial, P.TrueRow, false, [])]);
    }

    internal static async Task MissingAsync(FunctionalGateContext ctx, string id, CancellationToken token)
    {
        foreach (var text in new[] { P.ParentSelection, P.ChildSelection, P.IndependentSelection })
        {
            await PageAsync(await FunctionalGateRelationalCalls.SqlSdkAsync<QueryPage>(ctx, ctx.Reader,
                FunctionalGateRelationalCalls.Query(ctx, text, id), token), []);
        }
    }
}
