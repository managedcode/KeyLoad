namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class RestorePublicationRaceRefusal
{
    private const string Staging = ".keyload-restore-*";
    private const string NonemptyDestination = "Restore requires an empty destination.";

    internal static async Task RunAsync(EventingArtifactFixture fixture,
        RestorePublicationRaceAuthority authority, string backup, string target,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var occupied = new RestorePublicationOccupiedRegion(target);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var failure = Assert.ThrowsExactly<global::KeyLoad.KeyLoadException>(() =>
                    authority.Restore(fixture, backup, target, occupied.OccupyAfterReconciliation, token));
                await Assert.That(failure.Code).IsEqualTo(global::KeyLoad.ErrorCode.Conflict);
                await Assert.That(failure.Message).IsEqualTo(NonemptyDestination);
                await Assert.That(failure.StatusCode).IsEqualTo((int)global::System.Net.HttpStatusCode.Conflict);
                await Assert.That(failure.InnerException).IsNull();
                await occupied.RequireRetainedAsync(token);
                await Assert.That(Directory.EnumerateDirectories(fixture.Root, Staging).Any()).IsFalse();
            }, failures);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(occupied.RepairAsync, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(Directory.Exists(target)).IsTrue();
        await Assert.That(Directory.EnumerateFileSystemEntries(target).Any()).IsFalse();
    }
}
