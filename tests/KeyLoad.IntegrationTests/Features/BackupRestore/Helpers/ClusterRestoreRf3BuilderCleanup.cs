using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Retains actual pre-transfer failures and disposes the original native builder only on failure.</summary>
internal static class ClusterRestoreRf3BuilderCleanup
{
    internal static async Task RequireAsync(IDistributedApplicationTestingBuilder builder, Func<Task> actualStage)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(actualStage, failures).ConfigureAwait(false);
        if (failures.Count != EmptyFailures)
        { await ServerFailureObserver.ObserveAsync(() => builder.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task<DistributedApplication> BuildAsync(IDistributedApplicationTestingBuilder builder,
        Func<Task<DistributedApplication>> actualBuild)
    {
        DistributedApplication result = null!;
        await RequireAsync(builder, async () => result = await actualBuild().ConfigureAwait(false)).ConfigureAwait(false);
        return result;
    }

    private const int EmptyFailures = 0;
}
