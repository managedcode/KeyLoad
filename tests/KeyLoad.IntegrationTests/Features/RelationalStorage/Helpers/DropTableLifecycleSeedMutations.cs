using System.Collections.Immutable;
using P = KeyLoad.IntegrationTests.Features.RelationalStorage.DropTableLifecycleSeedProtocol;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleSeedMutations
{
    internal static ImmutableArray<Mutation> Create(DocumentResult anchor,
        ImmutableArray<ImmutableArray<DocumentResult>> rows)
    {
        var mutations = ImmutableArray.CreateBuilder<Mutation>();
        for (var table = P.First; table < P.TableCount; table++)
        {
            foreach (var row in rows[table])
            {
                mutations.Add(new PutDocument(row.Reference.Collection, row.Reference.Id, row.Json, P.Absent));
            }
        }

        mutations.Add(new PutDocument(P.AnchorCollection, P.AnchorId, P.AnchorJson, P.Absent));
        mutations.Add(new PutDocument(P.ControlTable, P.ControlId, P.ControlJson, P.Absent));
        for (var table = P.First; table < P.TableCount; table++)
        {
            mutations.Add(new UpsertEdge(P.Graph, P.Edges[table], anchor.Reference,
                rows[table][P.First].Reference, P.EdgeLabel));
        }

        return mutations.ToImmutable();
    }

    internal static ImmutableArray<MutationReceipt> Expected =>
    [new(P.PutKind, P.SdkTable, P.OldLeft, P.Initial), new(P.PutKind, P.SdkTable, P.OldRight, P.Initial),
        new(P.PutKind, P.McpTable, P.OldLeft, P.Initial), new(P.PutKind, P.McpTable, P.OldRight, P.Initial),
        new(P.PutKind, P.SqlTable, P.OldLeft, P.Initial), new(P.PutKind, P.SqlTable, P.OldRight, P.Initial),
        new(P.PutKind, P.AnchorCollection, P.AnchorId, P.Initial), new(P.PutKind, P.ControlTable, P.ControlId, P.Initial),
        new(P.EdgeKind, P.Graph, P.Edges[P.First], P.Initial), new(P.EdgeKind, P.Graph, P.Edges[P.Second], P.Initial),
        new(P.EdgeKind, P.Graph, P.Edges[P.Third], P.Initial)];
}
