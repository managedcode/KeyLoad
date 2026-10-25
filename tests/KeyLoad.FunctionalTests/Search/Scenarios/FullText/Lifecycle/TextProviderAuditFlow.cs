using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Search;

internal static class TextProviderAuditFlow
{
    internal static async Task RunAsync(TextProviderAuditDamage damage, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var owner = new TextProtectedRebuildColdOwner();
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(owner, damage, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(TextProtectedRebuildColdOwner owner,
        TextProviderAuditDamage damage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var state = new TextProviderAuditState();
        await TextProviderAuditProducer.SeedAsync(owner, state);
        await TextProviderAuditResults.CurrentAsync(owner, state, token);
        await TextProviderAuditProducer.ChangeAsync(owner, state);
        await TextProviderAuditResults.CurrentAsync(owner, state, token);
        await TextProviderAuditHistoryAssertions.AllAsync(owner, state, token);
        await TextProviderAuditCold.DamageAndRepairAsync(owner, state, damage, token);
        await TextProviderAuditCold.ReopenAsync(owner);
        await TextProviderAuditResults.CurrentAsync(owner, state, token);
        await TextProviderAuditHistoryAssertions.AllAsync(owner, state, token);
        await TextProviderAuditProducer.HealthyAsync(owner, state);
        await TextProviderAuditResults.CurrentAsync(owner, state, token);
        await TextProviderAuditHistoryAssertions.AllAsync(owner, state, token);
        await TextProviderAuditCold.ReopenAsync(owner);
        await TextProviderAuditResults.CurrentAsync(owner, state, token);
        await TextProviderAuditHistoryAssertions.AllAsync(owner, state, token);
    }
}
