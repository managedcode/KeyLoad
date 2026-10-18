using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Deliberately kills only the selected ephemeral native server process and joins its actual exit.</summary>
internal static class ProcessKillCommand
{
    private const string DisplayName = "Test abrupt process exit";
    private const string CompletedMessage = "The selected Aspire process was killed and its exit was joined.";
    private const int ArgumentCount = 3;
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
                    Input(ProcessFaultCommandProtocol.ExpectedProcessStartArgument)]
            });
    }

    private static InteractionInput Input(string name) => new()
    {
        Name = name, InputType = InputType.Text, Required = true,
        MaxLength = ProcessFaultCommandProtocol.MaximumIdentityCharacters
    };

    private static async Task<ExecuteCommandResult> ExecuteAsync(string resourceName, TimeSpan cleanupTimeout,
        ExecuteCommandContext context)
    {
        if (context.Arguments.Count != ArgumentCount
            || !Read(context, ProcessFaultCommandProtocol.ExpectedProcessIdArgument, out var pid)
            || pid > int.MaxValue
            || !Read(context, ProcessFaultCommandProtocol.ExpectedAspireStartArgument, out var aspireStart)
            || !Read(context, ProcessFaultCommandProtocol.ExpectedProcessStartArgument, out var processStart))
        {
            return CommandResults.Failure(ProcessFaultCommandProtocol.Refused);
        }
        var expected = new ProcessFaultCommandProtocol.Identity(resourceName, (int)pid, aspireStart, processStart);
        var notifications = context.Services.GetRequiredService<ResourceNotificationService>();
        if (ProjectProcessIdentity.Read(notifications, resourceName) != expected)
        {
            return CommandResults.Failure(ProcessFaultCommandProtocol.Refused);
        }
        using var process = Process.GetProcessById(expected.ProcessId);
        if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != expected.ProcessStartTimeUtcTicks
            || ProjectProcessIdentity.Read(notifications, resourceName) != expected)
        {
            return CommandResults.Failure(ProcessFaultCommandProtocol.Refused);
        }
        context.CancellationToken.ThrowIfCancellationRequested();
        process.Kill(entireProcessTree: true);
        using var deadline = new CancellationTokenSource(cleanupTimeout, TimeProvider.System);
        await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        var result = new ProcessFaultCommandProtocol.Result(resourceName, expected.ProcessId,
            expected.AspireStartTimeUtcTicks, expected.ProcessStartTimeUtcTicks, process.ExitCode);
        return CommandResults.Success(CompletedMessage, JsonSerializer.Serialize(result), CommandResultFormat.Json);
    }

    private static bool Read(ExecuteCommandContext context, string argument, out long value)
        => long.TryParse(context.Arguments.GetString(argument), NumberStyles.None, CultureInfo.InvariantCulture,
            out value) && value >= MinimumIdentity;
}
