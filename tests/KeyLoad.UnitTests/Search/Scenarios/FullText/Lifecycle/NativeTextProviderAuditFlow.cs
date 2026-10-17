using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextProviderAuditFlow
{
    internal static async Task RunAsync(NativeTextProviderAuditDamage damage, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var owner = new NativeTextProtectedRebuildColdOwner();
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(owner, damage, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(NativeTextProtectedRebuildColdOwner owner,
        NativeTextProviderAuditDamage damage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var state = new NativeTextProviderAuditState();
        await NativeTextProviderAuditProducer.SeedAsync(owner, state);
        await NativeTextProviderAuditResults.CurrentAsync(owner, state, token);
        await NativeTextProviderAuditProducer.ChangeAsync(owner, state);
        await NativeTextProviderAuditResults.CurrentAsync(owner, state, token);
        await NativeTextProviderAuditHistoryAssertions.AllAsync(owner, state, token);
        await NativeTextProviderAuditCold.DamageAndRepairAsync(owner, state, damage, token);
        await NativeTextProviderAuditCold.ReopenAsync(owner);
        await NativeTextProviderAuditResults.CurrentAsync(owner, state, token);
        await NativeTextProviderAuditHistoryAssertions.AllAsync(owner, state, token);
        await NativeTextProviderAuditProducer.HealthyAsync(owner, state);
        await NativeTextProviderAuditResults.CurrentAsync(owner, state, token);
        await NativeTextProviderAuditHistoryAssertions.AllAsync(owner, state, token);
        await NativeTextProviderAuditCold.ReopenAsync(owner);
        await NativeTextProviderAuditResults.CurrentAsync(owner, state, token);
        await NativeTextProviderAuditHistoryAssertions.AllAsync(owner, state, token);
    }
}
