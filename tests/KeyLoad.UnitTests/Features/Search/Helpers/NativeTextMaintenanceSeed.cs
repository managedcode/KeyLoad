namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextMaintenanceSeed
{
    internal static async Task<CommitReceipt> CommitAsync(TestDatabase database, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId,
                NativeTextBilingualAudit.UkrainianJson),
                new PutDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.EnglishId,
                    NativeTextBilingualAudit.EnglishJson)]);
        return await NativeTextMaintenanceCommit.ExecuteAsync<CommitReceipt>(database,
            OperationKind.Batch, command, command.CommandId, token);
    }
}
