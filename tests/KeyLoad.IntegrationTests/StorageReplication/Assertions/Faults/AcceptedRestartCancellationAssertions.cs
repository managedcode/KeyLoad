using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.IntegrationTests.Features.StorageReplication;

/// <summary>Proves interrupted execution health settlement preserves the actual accepted replacement.</summary>
internal static class AcceptedRestartCancellationAssertions
{
    internal static async Task InterruptAsync(ClusterFixture fixture, string resourceName,
        CancellationToken cancellationToken)
    {
        using var interrupted = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await interrupted.CancelAsync().ConfigureAwait(false);
        OperationCanceledException? original = null;
        try
        {
            await fixture.RestartServerAsync(resourceName, interrupted.Token);
        }
        catch (OperationCanceledException failure)
        {
            original = failure;
        }
        await Assert.That(original).IsNotNull();
        await Assert.That(original!.CancellationToken).IsEqualTo(interrupted.Token);
        await fixture.SaveFailureDiagnosticsAsync(cancellationToken);
    }

    internal static async Task SameReplacementAsync(ClusterFixture fixture, ProcessFaultCommandProtocol.LaunchIdentity accepted,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actual = ProjectProcessIdentity.ReadLaunch(fixture.App.ResourceNotifications, accepted.ResourceName);
        await Assert.That(actual).IsEqualTo(accepted);
    }
}
