using KeyLoad.Server;

namespace KeyLoad.UnitTests.Search;

internal static class AnnProviderQualificationFlow
{
    internal static async Task RunAsync(DistanceMetric metric, AnnProviderQualificationDamage damage, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var owner = new AnnProviderQualificationOwner();
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(owner, metric, damage, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(AnnProviderQualificationOwner owner, DistanceMetric metric,
        AnnProviderQualificationDamage damage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var state = new AnnProviderQualificationState(metric);
        await AnnProviderQualificationProducer.SeedAsync(owner, state, token);
        var original = AnnProviderQualificationCorpus.Build(owner, state, token);
        await AnnProviderQualificationResults.CurrentAsync(owner, state, original, token);
        await AnnProviderQualificationProducer.DeleteAsync(owner, state, token);
        await AnnProviderQualificationResults.CurrentAsync(owner, state,
            AnnProviderQualificationCorpus.Build(owner, state, token), token);
        var originalState = new AnnProviderQualificationState(metric);
        await AnnProviderQualificationResults.IndexAsync(owner, originalState, original, token);
        await AnnProviderQualificationProducer.ReinsertAsync(owner, state, token);
        var current = AnnProviderQualificationCorpus.Build(owner, state, token);
        await AnnProviderQualificationResults.CurrentAsync(owner, state, current, token);
        await AnnProviderQualificationResults.IndexAsync(owner, originalState, original, token);
        await AnnProviderQualificationHistoryAssertions.AllAsync(owner, state, token);
        await AnnProviderQualificationPersistence.SaveAsync(owner, state, current, token);
        await AnnProviderQualificationCold.DamageAndRepairAsync(owner, state, damage, current, token);
        await AnnProviderQualificationCold.ReopenAsync(owner, state, token);
        var savedState = new AnnProviderQualificationState(metric)
        {
            Deleted = true,
            Reinserted = true
        };
        await AnnProviderQualificationProducer.HealthyAsync(owner, state, token);
        await AnnProviderQualificationResults.CurrentAsync(owner, state,
            AnnProviderQualificationCorpus.Build(owner, state, token), token);
        await AnnProviderQualificationPersistence.SaveAsync(owner, state,
            AnnProviderQualificationCorpus.Build(owner, state, token), token);
        await AnnProviderQualificationHistoryAssertions.AllAsync(owner, state, token);
        await AnnProviderQualificationResults.IndexAsync(owner, originalState, original, token);
        await AnnProviderQualificationResults.IndexAsync(owner, savedState, current, token);
        await AnnProviderQualificationCold.ReopenAsync(owner, state, token);
    }
}
