using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Coordinates genuine Docker SIGKILL and Aspire restart with retained qualification receipts.</summary>
/// <param name="app">The actual fixture-owned distributed application.</param>
/// <param name="containerNames">Explicit container names selected from the Aspire model.</param>
/// <param name="repositoryRoot">The source checkout recorded with each restart receipt.</param>
internal sealed class ContainerRuntimeControl(
    DistributedApplication app,
    IReadOnlyDictionary<string, string> containerNames,
    string repositoryRoot)
{
    private readonly Dictionary<string, ContainerRuntimeKillReceipt> pendingReceipts = new(StringComparer.Ordinal);

    /// <summary>Kills the inspected full container ID and waits until the same named container is exited.</summary>
    /// <param name="resourceName">The actual Aspire resource to kill.</param>
    /// <param name="scenario">The unchanged qualification receipt scenario.</param>
    /// <param name="cancellationToken">The existing external operation cancellation.</param>
    public async Task KillAsync(string resourceName, string scenario, CancellationToken cancellationToken)
    {
        var containerName = GetContainerName(resourceName);
        var before = await ContainerRuntimeDocker.InspectAsync(containerName, cancellationToken);
        if (before.State != ContainerRuntimeProtocol.RunningState)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                ContainerRuntimeProtocol.BeforeKillFailure, resourceName, before.State));
        }

        var kill = await ContainerRuntimeDocker.RunAsync(
            [ContainerRuntimeProtocol.KillCommand, ContainerRuntimeProtocol.SignalArgument,
                ContainerRuntimeProtocol.KillSignal, before.Id], cancellationToken);
        ContainerRuntimeDocker.EnsureSuccessful(kill, ContainerRuntimeProtocol.KillOperation, resourceName);
        var stopped = await ContainerRuntimeDocker.WaitForExitedAsync(containerName, resourceName, cancellationToken);
        pendingReceipts[resourceName] = new(scenario, resourceName, containerName, before, stopped,
            kill.ExitCode, kill.StandardOutput, kill.StandardError);
    }

    /// <summary>Starts through Aspire, awaits healthy/running state and records a changed runtime start timestamp.</summary>
    /// <param name="resourceName">The actual Aspire resource with a retained verified kill receipt.</param>
    /// <param name="cancellationToken">The existing external operation cancellation.</param>
    public async Task RestartAsync(string resourceName, CancellationToken cancellationToken)
    {
        if (!pendingReceipts.TryGetValue(resourceName, out var receipt))
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                ContainerRuntimeProtocol.MissingKillFailure, resourceName));
        }

        var commands = app.Services.GetRequiredService<ResourceCommandService>();
        var command = await commands.ExecuteCommandAsync(resourceName, KnownResourceCommands.StartCommand, cancellationToken);
        if (!command.Success)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                ContainerRuntimeProtocol.StartFailure, resourceName, command.Message ?? ContainerRuntimeProtocol.UnknownCommandFailure));
        }

        await app.ResourceNotifications.WaitForResourceHealthyAsync(resourceName, cancellationToken);
        var after = await ContainerRuntimeDocker.InspectRunningAsync(receipt.ContainerName, resourceName, cancellationToken);
        var startedAtChanged = !string.Equals(receipt.Before.StartedAt, after.StartedAt, StringComparison.Ordinal);
        if (!startedAtChanged)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                ContainerRuntimeProtocol.UnchangedStartFailure, resourceName));
        }

        var sourceSha = await ContainerRuntimeReceiptStore.ReadSourceShaAsync(repositoryRoot, cancellationToken);
        var completed = new ContainerRuntimeRestartReceipt(receipt.Scenario, resourceName, receipt.ContainerName, receipt.Before.Id,
            receipt.Before.ConfigImage, receipt.Before.ImageId, receipt.Before.State, receipt.KillExitCode, receipt.KillOutput, receipt.KillError,
            receipt.Stopped.State, after.Id, after.ConfigImage, after.ImageId, after.State, receipt.Before.StartedAt,
            after.StartedAt, startedAtChanged, true, command.Message, sourceSha, repositoryRoot);
        await ContainerRuntimeReceiptStore.WriteAsync(completed, cancellationToken);
        pendingReceipts.Remove(resourceName);
    }

    private string GetContainerName(string resourceName) => containerNames.TryGetValue(resourceName, out var name)
        ? name
        : throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
            ContainerRuntimeProtocol.MissingContainerFailure, resourceName));
}
