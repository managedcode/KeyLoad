using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class RuntimeJournalReadCapabilities
{
    internal static object? Execute(DatabaseEngine database, string principalId, GrainReadKind kind,
        object payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return kind switch
        {
            GrainReadKind.RuntimeJournalHeader => database.GetRuntimeJournalHeader(principalId,
                GrainPayload.Read<string>(payload), cancellationToken),
            GrainReadKind.RuntimeJournalPage => database.ReadRuntimeJournal(principalId,
                GrainPayload.Read<RuntimeJournalReadRequest>(payload), cancellationToken),
            GrainReadKind.RuntimeJournalCatalog => Catalog(database, principalId, payload, cancellationToken),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    private static RuntimeJournalCatalog Catalog(DatabaseEngine database, string principalId,
        object payload, CancellationToken cancellationToken)
    {
        GrainPayload.RequireNoDto(payload);
        return database.ReadRuntimeJournalCatalog(principalId, cancellationToken);
    }
}
