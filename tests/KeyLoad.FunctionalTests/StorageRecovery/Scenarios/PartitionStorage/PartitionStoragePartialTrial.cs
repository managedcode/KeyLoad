using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class PartitionStoragePartialTrial
{
    private const int NoFailures = 0;

    internal static async Task<PartitionStoragePartialStart> RunAsync(string[] arguments,
        RequestCqrsHostedProfile profile, CancellationToken token)
    {
        var failures = new List<Exception>();
        WebApplication? application = null;
        PartitionHostApplicationOwner? owner = null;
        PartitionStoragePartialStart? observation = null;
        PartitionStorageLifecycle? lower = null;
        OrleansNode? node = null;
        var refusalProved = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(profile.Timing.StartupTimeout, TimeProvider.System);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, token);
            application = await ServerConfiguration.BuildAsync(arguments,
                lifecycleParticipantFactory: provider => observation = new(
                    provider.GetRequiredService<PartitionHostApplicationOwner>()),
                configureServices: profile.Register, startupCancellation: startup.Token).ConfigureAwait(false);
            owner = application.Services.GetRequiredService<PartitionHostApplicationOwner>();
            lower = application.Services.GetRequiredService<PartitionStorageLifecycle>();
            node = application.Services.GetRequiredService<OrleansNode>();
            var originalStart = application.StartAsync(startup.Token);
            var expected = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => originalStart, expected).ConfigureAwait(false);
            var proof = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => PartitionStoragePartialAssertions.RequireAsync(
                observation!, owner, originalStart, expected), proof).ConfigureAwait(false);
            if (proof.Count != NoFailures)
            {
                expected.AddRange(proof);
                ServerFailureObserver.ThrowIfAny(expected);
            }
            refusalProved = true;
        }, failures).ConfigureAwait(false);
        if (application is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => PartitionStoragePartialClose.RunAsync(
                application, node, owner, lower, refusalProved, failures), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return observation!;
    }
}
