using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Retains one actual native Start acceptance independently of later readiness settlement.</summary>
internal sealed class ContainerRestartOwnership
{
    internal const string PendingFailure = "An unsettled native restart owner must be retained.";
    private const string UnknownStartFailure = "The original native Start outcome is unobserved; owned shutdown is required.";
    private const string ReplacedFailure = "The accepted native restart replacement identity changed before settlement.";
    private bool attempted;
    private ContainerRuntimeInspection? replacement;
    internal bool StartAccepted { get; private set; }
    internal string? StartMessage { get; private set; }

    internal async Task<ContainerRuntimeInspection> AcceptAsync(DistributedApplication app,
        ContainerRuntimeKillReceipt receipt, ContainerRestartFailureCapture capture,
        CancellationToken cancellationToken)
    {
        if (StartAccepted)
        { capture.StartRetained(); }
        if (!StartAccepted)
        {
            if (attempted)
            { throw new InvalidOperationException(UnknownStartFailure); }
            cancellationToken.ThrowIfCancellationRequested();
            var commands = app.Services.GetRequiredService<ResourceCommandService>();
            attempted = true;
            var command = await commands.ExecuteCommandAsync(receipt.ResourceName,
                KnownResourceCommands.StartCommand, cancellationToken);
            if (!command.Success)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    ContainerRuntimeProtocol.StartFailure, receipt.ResourceName,
                    command.Message ?? ContainerRuntimeProtocol.UnknownCommandFailure));
            }
            StartAccepted = true;
            StartMessage = command.Message;
            capture.StartSucceeded();
        }
        if (replacement is not null)
        { return replacement; }
        var observed = await ContainerRuntimeDocker.InspectRunningAsync(receipt.ContainerName,
            receipt.ResourceName, cancellationToken);
        if (observed.StartedAt == receipt.Before.StartedAt
            || observed.ImageId != receipt.Before.ImageId || observed.ConfigImage != receipt.Before.ConfigImage)
        { throw new InvalidOperationException(ReplacedFailure); }
        replacement = observed;
        RequireSameReplacement(observed);
        return observed;
    }

    internal void RequireSameReplacement(ContainerRuntimeInspection observed)
    {
        if (replacement is null || replacement != observed)
        { throw new InvalidOperationException(ReplacedFailure); }
    }
}
