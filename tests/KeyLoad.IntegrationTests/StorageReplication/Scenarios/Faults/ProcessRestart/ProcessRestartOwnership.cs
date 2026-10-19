using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.StorageReplication;

/// <summary>Retains one actual execution Start acceptance independently of later readiness settlement.</summary>
internal sealed class ProcessRestartOwnership
{
    internal const string PendingFailure = "An unsettled execution restart owner must be retained.";
    private const string UnknownStartFailure = "The original execution Start outcome is unobserved; owned shutdown is required.";
    private bool attempted;
    private ProcessFaultCommandProtocol.LaunchIdentity? replacement;
    internal bool StartAccepted { get; private set; }
    internal string? StartMessage { get; private set; }

    internal async Task<ProcessFaultCommandProtocol.LaunchIdentity> AcceptAsync(DistributedApplication app,
        ProcessRuntimeKillReceipt receipt, ProcessRestartFailureCapture capture, CancellationToken cancellationToken)
    {
        if (StartAccepted)
        {
            capture.StartRetained();
        }
        if (!StartAccepted)
        {
            if (attempted)
            {
                throw new InvalidOperationException(UnknownStartFailure);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var commands = app.Services.GetRequiredService<ResourceCommandService>();
            attempted = true;
            var command = await commands.ExecuteCommandAsync(receipt.ResourceName,
                KnownResourceCommands.StartCommand, cancellationToken);
            if (!command.Success)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    ProcessRuntimeProtocol.StartFailure, receipt.ResourceName,
                    command.Message ?? ProcessRuntimeProtocol.UnknownCommandFailure));
            }
            StartAccepted = true;
            StartMessage = command.Message;
            capture.StartSucceeded();
        }
        if (replacement is not null)
        {
            return replacement;
        }
        var observed = await ProcessRestartRuntimeObservation.ReadAsync(app, receipt, cancellationToken);
        replacement = observed;
        return observed;
    }

    internal void RequireSameReplacement(ProcessFaultCommandProtocol.LaunchIdentity observed)
    {
        if (replacement is null || replacement != observed)
        {
            throw new InvalidOperationException(ProcessRuntimeProtocol.ReplacedFailure);
        }
    }
}
