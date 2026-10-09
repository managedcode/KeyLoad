using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Original operator settlement and six-resource readiness, including explicit offline admission.</summary>
internal static class ClusterRestoreRf3StartupCompletion
{
    internal static async Task WaitAsync(ClusterRestoreRf3Fixture owner,
        IResourceBuilder<ExecutableResource>? cli, NativeClusterRestoreStage? cut, bool operatorOnly,
        ErrorCode? expectedFailure, string? exactFailureDetail, CancellationToken cancellationToken)
    {
        if (cli is not null && cut is not null)
        {
            await (owner.HeldObservation ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid))
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        if (cli is not null)
        {
            var terminal = await owner.Application.ResourceNotifications.WaitForResourceAsync(
                ClusterRestoreRf3Protocol.RestoreResource, value => value.Snapshot.ExitCode is not null,
                cancellationToken).ConfigureAwait(false);
            owner.RecordOperatorExit(terminal.Snapshot.ExitCode ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid));
            if (expectedFailure is { } rejected)
            {
                await owner.RequireRejectedServersAsync(rejected, exactFailureDetail, cancellationToken).ConfigureAwait(false);
                return;
            }
            await Assert.That(terminal.Snapshot.ExitCode).IsEqualTo(ClusterRestoreRf3Protocol.SuccessfulExit);
            owner.AdmitServers(!operatorOnly);
        }
        if (operatorOnly)
        {
            foreach (var node in ClusterRestoreRf3Protocol.Nodes)
            {
                var stopped = await owner.Application.ResourceNotifications.WaitForResourceAsync(node,
                    value => value.Snapshot.State?.Text == KnownResourceStates.NotStarted, cancellationToken).ConfigureAwait(false);
                await Assert.That(stopped.Snapshot.State?.Text).IsEqualTo(KnownResourceStates.NotStarted);
            }
            return;
        }
        foreach (var node in ClusterRestoreRf3Protocol.Nodes)
        { await owner.Application.ResourceNotifications.WaitForResourceHealthyAsync(node, cancellationToken).ConfigureAwait(false); }
        await TwoRf3MembershipDataReadiness.WaitAsync(owner.Application,
            owner.Application.Services.GetRequiredService<IOptions<TestExecutionOptions>>(), TimeProvider.System,
            cancellationToken).ConfigureAwait(false);
    }
}
