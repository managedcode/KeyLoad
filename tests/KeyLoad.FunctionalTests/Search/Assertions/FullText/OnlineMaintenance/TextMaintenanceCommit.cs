namespace KeyLoad.FunctionalTests.Search;

/// <summary>Uses the existing execution TestDatabase replica admission without a second writer or apply owner.</summary>
internal static class TextMaintenanceCommit
{
    internal static async Task<T> ExecuteAsync<T>(TestDatabase database,
        OperationKind kind, object request, Guid commandId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = database.Store.Position;
        var result = database.Submit(kind, request, id: commandId).Get<T>();
        var after = database.Store.Position;
        var replay = database.Submit(kind, request, id: commandId).Get<T>();
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(result))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(after);
        await Assert.That(after).IsGreaterThanOrEqualTo(before);
        return result;
    }
}
