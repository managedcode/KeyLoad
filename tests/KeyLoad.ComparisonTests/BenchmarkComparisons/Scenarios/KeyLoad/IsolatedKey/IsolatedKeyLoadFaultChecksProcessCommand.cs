using System.Globalization;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Invokes only the AppHost-owned abrupt process command and verifies its actual joined identity.</summary>
internal static class IsolatedKeyLoadFaultChecksProcessCommand
{
    internal static async Task<ProcessFaultCommandProtocol.Result> KillAsync(DistributedApplication app,
        NativeProcessEvidence before, CancellationToken token)
    {
        var expected = new ProcessFaultCommandProtocol.Identity(before.Name, before.ProcessId,
            (before.AspireStartedAtUtc ?? throw new InvalidOperationException(IsolatedKeyLoadFaultChecksProtocol.Failure)).Ticks,
            before.StartedAtUtc.Ticks,
            before.AspireProcessId ?? throw new InvalidOperationException(IsolatedKeyLoadFaultChecksProtocol.Failure),
            (before.AspireProcessStartedAtUtc ?? throw new InvalidOperationException(IsolatedKeyLoadFaultChecksProtocol.Failure)).Ticks);
        var arguments = new InteractionInputCollection([
            Input(ProcessFaultCommandProtocol.ExpectedProcessIdArgument, expected.ProcessId),
            Input(ProcessFaultCommandProtocol.ExpectedAspireStartArgument, expected.AspireStartTimeUtcTicks),
            Input(ProcessFaultCommandProtocol.ExpectedProcessStartArgument, expected.ProcessStartTimeUtcTicks),
            Input(ProcessFaultCommandProtocol.ExpectedLaunchProcessIdArgument, expected.LaunchProcessId),
            Input(ProcessFaultCommandProtocol.ExpectedLaunchProcessStartArgument, expected.LaunchProcessStartTimeUtcTicks)
        ]);
        var command = await app.Services.GetRequiredService<ResourceCommandService>().ExecuteCommandAsync(
            before.Name, ProcessFaultCommandProtocol.CommandName, arguments, token);
        if (command.Canceled)
        {
            token.ThrowIfCancellationRequested();
            throw new OperationCanceledException(IsolatedKeyLoadFaultChecksProtocol.Failure, token);
        }
        var data = command.Data;
        IsolatedKeyLoadFaultChecksProtocol.Require(command.Success && data is not null
            && data.Format == CommandResultFormat.Json && data.Value.Length <= ProcessFaultCommandProtocol.MaximumResultCharacters);
        var killed = JsonSerializer.Deserialize<ProcessFaultCommandProtocol.Result>(data!.Value)
            ?? throw new InvalidOperationException(IsolatedKeyLoadFaultChecksProtocol.Failure);
        IsolatedKeyLoadFaultChecksProtocol.Require(killed.ResourceName == expected.ResourceName
            && killed.ProcessId == expected.ProcessId && killed.AspireStartTimeUtcTicks == expected.AspireStartTimeUtcTicks
            && killed.ProcessStartTimeUtcTicks == expected.ProcessStartTimeUtcTicks
            && killed.LaunchProcessId == expected.LaunchProcessId
            && killed.LaunchProcessStartTimeUtcTicks == expected.LaunchProcessStartTimeUtcTicks
            && killed.LaunchExitCode != 0
            && (OperatingSystem.IsWindows() ? killed.ExitCode is not null and not 0 : killed.ExitCode is null));
        return killed;
    }

    private static InteractionInput Input(string name, long value) => new()
    {
        Name = name,
        InputType = InputType.Text,
        Value = value.ToString(CultureInfo.InvariantCulture)
    };
}
