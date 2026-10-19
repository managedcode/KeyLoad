using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

/// <summary>Deliberately kills only the selected ephemeral execution server process and joins its actual exit.</summary>
internal static class ProcessKillCommand
{
    private const string DisplayName = "Test abrupt process exit";
    private const string CompletedMessage = "The selected Aspire process was killed and its exit was joined.";
    private const int ArgumentCount = 5;
    private const int MinimumIdentity = 1;

    internal static void Apply(IDistributedApplicationBuilder builder, IResourceBuilder<ProjectResource> resource,
        bool ephemeral)
    {
        var runtime = AppHostOptionsRegistration.Get(builder);
        if (!runtime.Control.Value.ProcessFaults)
        {
            return;
        }
        if (!ephemeral)
        {
            throw new InvalidOperationException(AppHostControlOptionsRegistration.InvalidProcessFaultSelection);
        }
        resource.WithCommand(ProcessFaultCommandProtocol.CommandName, DisplayName,
            context => ExecuteAsync(resource.Resource.Name, runtime.TestExecution.Value.ApplicationCleanupTimeout, context),
            new CommandOptions
            {
                Visibility = ResourceCommandVisibility.Api,
                Arguments = [Input(ProcessFaultCommandProtocol.ExpectedProcessIdArgument),
                    Input(ProcessFaultCommandProtocol.ExpectedAspireStartArgument),
                    Input(ProcessFaultCommandProtocol.ExpectedProcessStartArgument),
                    Input(ProcessFaultCommandProtocol.ExpectedLaunchProcessIdArgument),
                    Input(ProcessFaultCommandProtocol.ExpectedLaunchProcessStartArgument)]
            });
    }

    private static InteractionInput Input(string name) => new()
    {
        Name = name,
        InputType = InputType.Text,
        Required = true,
        MaxLength = ProcessFaultCommandProtocol.MaximumIdentityCharacters
    };

    private static async Task<ExecuteCommandResult> ExecuteAsync(string resourceName, TimeSpan cleanupTimeout,
        ExecuteCommandContext context)
    {
        if (context.Arguments.Count != ArgumentCount
            || !Read(context, ProcessFaultCommandProtocol.ExpectedProcessIdArgument, out var pid)
            || pid > int.MaxValue
            || !Read(context, ProcessFaultCommandProtocol.ExpectedAspireStartArgument, out var aspireStart)
            || !Read(context, ProcessFaultCommandProtocol.ExpectedProcessStartArgument, out var processStart)
            || !Read(context, ProcessFaultCommandProtocol.ExpectedLaunchProcessIdArgument, out var launchPid)
            || launchPid > int.MaxValue
            || !Read(context, ProcessFaultCommandProtocol.ExpectedLaunchProcessStartArgument, out var launchStart))
        {
            return CommandResults.Failure(ProcessFaultCommandProtocol.Refused);
        }
        var expected = new ProcessFaultCommandProtocol.Identity(resourceName, (int)pid, aspireStart, processStart, (int)launchPid, launchStart);
        var notifications = context.Services.GetRequiredService<ResourceNotificationService>();
        var options = context.Services.GetRequiredService<IOptions<TestExecutionOptions>>();
        if (ProjectProcessIdentity.ReadLaunch(notifications, resourceName) != expected.Launch)
        {
            return CommandResults.Failure(ProcessFaultCommandProtocol.Refused);
        }
        var observed = await ProjectProcessEvidence.ReadAsync(notifications, resourceName,
            options, context.CancellationToken).ConfigureAwait(false);
        if (observed.ProcessId != expected.ProcessId || observed.StartedAtUtc.Ticks != expected.ProcessStartTimeUtcTicks
            || observed.AspireProcessId != expected.LaunchProcessId
            || observed.AspireProcessStartedAtUtc?.Ticks != expected.LaunchProcessStartTimeUtcTicks)
        {
            return CommandResults.Failure(ProcessFaultCommandProtocol.Refused);
        }
        using var process = Process.GetProcessById(expected.ProcessId);
        using var launch = Process.GetProcessById(expected.LaunchProcessId);
        context.CancellationToken.ThrowIfCancellationRequested();
        if (await ProjectProcessIdentity.ReadAsync(notifications, resourceName, expected.ProcessId,
            options, context.CancellationToken).ConfigureAwait(false) != expected)
        {
            return CommandResults.Failure(ProcessFaultCommandProtocol.Refused);
        }
        launch.Kill(entireProcessTree: true);
        using var deadline = new CancellationTokenSource(cleanupTimeout, TimeProvider.System);
        await Task.WhenAll(process.WaitForExitAsync(deadline.Token), launch.WaitForExitAsync(deadline.Token))
            .ConfigureAwait(false);
        var launchExitCode = await ProjectProcessExitObservation.ReadAsync(notifications, expected, deadline.Token)
            .ConfigureAwait(false);
        int? serverExitCode = OperatingSystem.IsWindows() ? process.ExitCode : null;
        var result = new ProcessFaultCommandProtocol.Result(resourceName, expected.ProcessId,
            expected.AspireStartTimeUtcTicks, expected.ProcessStartTimeUtcTicks, expected.LaunchProcessId,
            expected.LaunchProcessStartTimeUtcTicks, serverExitCode, launchExitCode);
        return CommandResults.Success(CompletedMessage, JsonSerializer.Serialize(result), CommandResultFormat.Json);
    }

    private static bool Read(ExecuteCommandContext context, string argument, out long value)
        => long.TryParse(context.Arguments.GetString(argument), NumberStyles.None, CultureInfo.InvariantCulture,
            out value) && value >= MinimumIdentity;
}
