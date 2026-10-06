using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class RuntimeJournalReadCapabilities
{
    internal static object? Execute(DatabaseEngine database, string principalId, GrainReadKind kind,
        ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return kind switch
        {
            GrainReadKind.RuntimeJournalHeader => database.GetRuntimeJournalHeader(principalId,
                GrainNativePayload.Read<string>(payload), cancellationToken),
            GrainReadKind.RuntimeJournalPage => database.ReadRuntimeJournal(principalId,
                GrainNativePayload.Read<RuntimeJournalReadRequest>(payload), cancellationToken),
            GrainReadKind.RuntimeJournalCatalog => Catalog(database, principalId, payload, cancellationToken),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    private static RuntimeJournalCatalog Catalog(DatabaseEngine database, string principalId,
        ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        GrainNativePayload.RequireNoDto(payload);
        return database.ReadRuntimeJournalCatalog(principalId, cancellationToken);
    }
}
