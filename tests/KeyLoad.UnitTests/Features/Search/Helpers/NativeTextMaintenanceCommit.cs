namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Uses the existing native TestDatabase replica admission without a second writer or apply owner.</summary>
internal static class NativeTextMaintenanceCommit
{
    internal static async Task<T> ExecuteAsync<T>(TestDatabase database,
        OperationKind kind, object request, Guid commandId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = database.Store.Position;
        var result = database.Submit(kind, request, id: commandId).Get<T>();
        var after = database.Store.Position;
        var replay = database.Submit(kind, request, id: commandId).Get<T>();
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(result))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(after);
        await Assert.That(after).IsGreaterThanOrEqualTo(before);
        return result;
    }
}
