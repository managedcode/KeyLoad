using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionHostApplicationFlow
{
    internal static async Task RunAsync(bool heldLock)
    {
        var failures = new List<Exception>();
        var sessions = new List<PartitionHostApplicationSession>();
        PartitionHostRecoveryFixture? fixture = null;
        ServerFailureObserver.Observe(() => fixture = new(), failures);
        if (fixture is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                if (heldLock)
                {
                    await RefuseAsync(fixture, sessions).ConfigureAwait(false);
                }
                await HealthyAsync(fixture, sessions).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
            foreach (var session in sessions)
            {
                await session.CloseAsync(failures).ConfigureAwait(false);
            }
            if (failures.Count == PartitionHostApplicationProtocol.NoFailures &&
                sessions.All(session => session.Owner.CanReleaseRoot))
            {
                ServerFailureObserver.Observe(fixture.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RefuseAsync(PartitionHostRecoveryFixture fixture,
        List<PartitionHostApplicationSession> sessions)
    {
        Directory.CreateDirectory(fixture.Options.DataDirectory);
        var failures = new List<Exception>();
        FileStream? held = null;
        ServerFailureObserver.Observe(() => held = new FileStream(
            Path.Combine(fixture.Options.DataDirectory, PartitionHostApplicationProtocol.OwnershipFile),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None), failures);
        if (held is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var session = await PartitionHostApplicationSession.CreateAsync(fixture.Options).ConfigureAwait(false);
                sessions.Add(session);
                await PartitionHostApplicationLock.RefuseAsync(session, fixture.Options.DataDirectory);
                await session.CloseAsync(failures);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(held.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task HealthyAsync(PartitionHostRecoveryFixture fixture,
        List<PartitionHostApplicationSession> sessions)
    {
        var session = await PartitionHostApplicationSession.CreateAsync(fixture.Options).ConfigureAwait(false);
        sessions.Add(session);
        var host = await PartitionHostApplicationConcurrency.OpenAsync(session.Owner, session.Context).ConfigureAwait(false);
        await PartitionHostApplicationLock.ExcludedAsync(fixture.Options.DataDirectory);
        var cut = PartitionHostApplicationCut.Capture(host.Database.Store);
        cut = await cut.CommitAsync(host.Database.Store, PartitionHostApplicationProtocol.FirstKey,
            PartitionHostApplicationProtocol.FirstValue).ConfigureAwait(false);
        cut = await cut.CommitAsync(host.Database.Store, PartitionHostApplicationProtocol.SecondKey,
            PartitionHostApplicationProtocol.SecondValue).ConfigureAwait(false);
        await PartitionHostApplicationConcurrency.CloseAsync(session.Owner, session.Context).ConfigureAwait(false);
        for (var cold = PartitionHostApplicationProtocol.FirstCold; cold < PartitionHostApplicationProtocol.ColdCycles; cold++)
        {
            var reopened = await PartitionHostApplicationSession.CreateAsync(fixture.Options).ConfigureAwait(false);
            sessions.Add(reopened);
            var recovered = await PartitionHostApplicationConcurrency.OpenAsync(reopened.Owner, reopened.Context).ConfigureAwait(false);
            await cut.RequireAsync(recovered.Database.Store).ConfigureAwait(false);
            await PartitionHostApplicationLock.ExcludedAsync(fixture.Options.DataDirectory);
            await PartitionHostApplicationConcurrency.CloseAsync(reopened.Owner, reopened.Context).ConfigureAwait(false);
        }
    }
}
