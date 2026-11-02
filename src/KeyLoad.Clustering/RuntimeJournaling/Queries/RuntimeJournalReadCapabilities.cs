using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class RuntimeJournalReadCapabilities
{
    internal static GrainValue Execute(DatabaseEngine database, string principalId, GrainReadKind kind,
        DecodedGrainRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return kind switch
        {
            GrainReadKind.RuntimeJournalHeader => new GrainValue(database.GetRuntimeJournalHeader(principalId,
                (GrainPayload.Read<string>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as string at this closed operation boundary before executing typed code. */), cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this RuntimeJournalHeader response with its generated runtime type header. */,
            GrainReadKind.RuntimeJournalPage => new GrainValue(database.ReadRuntimeJournal(principalId,
                (GrainPayload.Read<RuntimeJournalReadRequest>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.RuntimeJournalReadRequest at this closed operation boundary before executing typed code. */), cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this RuntimeJournalPage response with its generated runtime type header. */,
            GrainReadKind.RuntimeJournalCatalog => new GrainValue(Catalog(database, principalId, request, cancellationToken)) /* KLD0041: The current GrainValue wire slot carries this RuntimeJournalCatalog response with its generated runtime type header. */,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    private static RuntimeJournalCatalog Catalog(DatabaseEngine database, string principalId,
        DecodedGrainRequest request, CancellationToken cancellationToken)
    {
        GrainPayload.RequireNoDto((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the current generated envelope scalar NoDto marker for this capability before executing a request without a DTO. */;
        return database.ReadRuntimeJournalCatalog(principalId, cancellationToken);
    }
}
