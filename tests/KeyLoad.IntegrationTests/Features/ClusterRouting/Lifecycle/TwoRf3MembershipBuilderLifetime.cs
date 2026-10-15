using Aspire.Hosting.Testing;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns the exact native testing builder until application ownership transfers.</summary>
internal sealed class TwoRf3MembershipBuilderLifetime
{
    private IDistributedApplicationTestingBuilder? builder;
    private Task? disposal;

    internal bool RetentionVeto
    {
        get; private set;
    }

    internal Exception? OriginalStartupFailure
    {
        get; private set;
    }

    internal async Task RunAsync(TwoRf3MembershipWave wave, Func<Task> startup)
    {
        OriginalStartupFailure = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            Begin(wave);
            await startup().ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (failures.Count > 0)
        {
            OriginalStartupFailure = failures[0];
            Retain(wave, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void Begin(TwoRf3MembershipWave wave)
    {
        if (builder is not null || wave.application is not null || RetentionVeto)
        {
            throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
        }
        wave.applicationDisposed = false;
        wave.nodeLocksReleased = false;
        wave.startAttempted = false;
    }

    internal void Register(IDistributedApplicationTestingBuilder owned)
    {
        builder = owned;
    }

    internal void Transfer()
    {
        builder = null;
    }

    internal async Task DisposeBeforeTransferAsync(TwoRf3MembershipWave wave, List<Exception> failures)
    {
        if (builder is not { } owned)
        {
            return;
        }
        Retain(wave, failures);
        disposal ??= DisposeNativeAsync(owned);
        var originalDisposal = disposal;
        await ServerFailureObserver.ObserveAsync(() => originalDisposal, failures).ConfigureAwait(false);
    }

    internal static void RetainControls(TwoRf3MembershipWave wave)
    {
        wave.queryControls?.RetainEvidence();
        wave.nativeMembershipControls?.RetainEvidence();
        wave.canonicalCutControls?.RetainEvidence();
    }

    private void Retain(TwoRf3MembershipWave wave, List<Exception> failures)
    {
        if (RetentionVeto)
        {
            return;
        }
        RetentionVeto = true;
        ServerFailureObserver.Observe(wave.RetainRoots, failures);
    }

    private static async Task DisposeNativeAsync(IDistributedApplicationTestingBuilder owned)
    {
        await owned.DisposeAsync().ConfigureAwait(false);
    }
}
