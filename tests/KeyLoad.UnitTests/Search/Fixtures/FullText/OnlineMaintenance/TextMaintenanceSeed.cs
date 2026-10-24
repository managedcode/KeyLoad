namespace KeyLoad.UnitTests.Search;

internal static class TextMaintenanceSeed
{
    internal static async Task<CommitReceipt> CommitAsync(TestDatabase database, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(TextBilingualAudit.Collection, TextBilingualAudit.UkrainianId,
                TextBilingualAudit.UkrainianJson),
                new PutDocument(TextBilingualAudit.Collection, TextBilingualAudit.EnglishId,
                    TextBilingualAudit.EnglishJson)]);
        return await TextMaintenanceCommit.ExecuteAsync<CommitReceipt>(database,
            OperationKind.Batch, command, command.CommandId, token);
    }
}
