using P = KeyLoad.IntegrationTests.Features.Authorization.RowRevisionInferenceClusterProtocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RowRevisionInferenceClusterRequests
{
    internal static CommandRequest Create(PartitionRef partition, string kind, string id, long revision)
    {
        Mutation mutation = kind switch
        {
            P.Put => new PutDocument(P.Collection, id, P.Changed, revision,
                new(OwnerId: P.Reader), ExplicitReplacement: true),
            P.Patch => new PatchDocument(P.Collection, id, [new(P.ValuePath, PatchKind.Set, P.ValueJson)], revision),
            P.Delete => new DeleteDocument(P.Collection, id, revision),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return new(Guid.NewGuid(), partition, [mutation]);
    }

    internal static string ReceiptKind(string kind) => kind switch
    {
        P.Put => P.PutReceipt,
        P.Patch => P.PatchReceipt,
        P.Delete => P.DeleteReceipt,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
