using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Joins the exact fixture-owned held CLI process, its terminal observation and original AppHost readers.</summary>
internal static class ClusterRestoreRf3HeldShutdown
{
    internal static async Task JoinAsync(ClusterRestoreRf3Fixture owner, NativeClusterRestoreStage stage,
        CancellationToken cancellationToken)
    {
        var held = owner.HeldObservation ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);
        var pid = await held.WaitAsync(cancellationToken).ConfigureAwait(false);
        var current = await owner.Application.ResourceNotifications.WaitForResourceAsync(
            ClusterRestoreRf3Protocol.RestoreResource, value => value.Snapshot.ExitCode is null,
            cancellationToken).ConfigureAwait(false);
        await Assert.That(current.Snapshot.ExitCode).IsNull();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ClusterRestoreRf3StageProcess.KillAndJoinAsync(pid,
            ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, owner.OperationId, stage,
            owner.Application.Services.GetRequiredService<IOptions<TestExecutionOptions>>().Value.CleanupOutputCharacters,
            cancellationToken), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var terminal = await owner.Application.ResourceNotifications.WaitForResourceAsync(
                ClusterRestoreRf3Protocol.RestoreResource, value => value.Snapshot.ExitCode is not null,
                cancellationToken).ConfigureAwait(false);
            owner.RecordOperatorExit(terminal.Snapshot.ExitCode ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid));
            await Assert.That(owner.OriginalOperatorExitCode).IsNotEqualTo(ClusterRestoreRf3Protocol.SuccessfulExit);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(owner.StopAsync, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

}
