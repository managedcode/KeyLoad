using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedQueryAuthorityCold
{
    private const string MissingCut = "The original full six-owner canonical cut was not retained.";
    private const string MissingIdentities = "The original full six-owner storage identities were not retained.";

    internal static async Task RunAsync(TwoRf3MembershipWave wave, DistributedQueryAuthorityState state,
        List<Exception> failures, CancellationToken token)
    {
        string[][]? original = null;
        await ServerFailureObserver.ObserveAsync(async () => original = await DistributedSearchRf3RawCut.StopAndReadAsync(wave, token), failures);
        byte[][]? identities = null;
        await ServerFailureObserver.ObserveAsync(() =>
        {
            identities = DistributedQueryAuthorityIdentity.Capture(wave);
            return Task.CompletedTask;
        }, failures);
        for (var ordinal = DistributedQueryAuthorityProtocol.FirstCold; ordinal <= DistributedQueryAuthorityProtocol.LastCold; ordinal += DistributedQueryAuthorityProtocol.ColdStep)
        {
            await ServerFailureObserver.ObserveAsync(() => wave.RestartJoinedAsync(token), failures);
            await ServerFailureObserver.ObserveAsync(() => WaveAsync(wave, state, token), failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var actual = await DistributedSearchRf3RawCut.StopAndReadAsync(wave, token);
                if (original is null)
                {
                    throw new InvalidOperationException(MissingCut);
                }
                await SqlRf3Protocol.EqualAsync(original, actual);
            }, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var actual = DistributedQueryAuthorityIdentity.Capture(wave);
                if (identities is null)
                {
                    throw new InvalidOperationException(MissingIdentities);
                }
                await Assert.That(actual.Length).IsEqualTo(identities.Length);
                for (var index = DistributedQueryAuthorityProtocol.FirstVoter; index < actual.Length; index++)
                {
                    await Assert.That(actual[index].AsSpan().SequenceEqual(identities[index])).IsTrue();
                }
            }, failures);
        }
    }
    private static async Task WaveAsync(TwoRf3MembershipWave wave, DistributedQueryAuthorityState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var callers = new DistributedSearchRf3Callers(wave);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var context = await DistributedQueryAuthoritySetup.ContextAsync(wave, callers, state, token);
                await DistributedQueryAuthorityPages.HealthyAsync(context, state, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
