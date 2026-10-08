namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Proves interrupted native health settlement preserves the actual accepted replacement.</summary>
internal static class AcceptedRestartCancellationAssertions
{
    internal static async Task InterruptAsync(ClusterFixture fixture, string resourceName,
        CancellationToken cancellationToken)
    {
        using var interrupted = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await interrupted.CancelAsync().ConfigureAwait(false);
        OperationCanceledException? original = null;
        try
        { await fixture.RestartContainerAsync(resourceName, interrupted.Token); }
        catch (OperationCanceledException failure)
        { original = failure; }
        await Assert.That(original).IsNotNull();
        await Assert.That(original!.CancellationToken).IsEqualTo(interrupted.Token);
        await fixture.SaveFailureDiagnosticsAsync(cancellationToken);
    }

    internal static async Task SameReplacementAsync(ContainerRuntimeInspection accepted,
        CancellationToken cancellationToken)
    {
        var actual = await ContainerRuntimeDocker.InspectRunningAsync(accepted.Id,
            accepted.Id, cancellationToken);
        await Assert.That(actual).IsEqualTo(accepted);
    }
}
