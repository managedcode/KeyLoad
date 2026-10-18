using System.Globalization;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Invokes the original Aspire resource command and retains the exact killed process identity and exit.</summary>
internal static class AspireProcessFaultCommand
{
    internal static async Task<ProcessFaultCommandProtocol.Result> KillAsync(DistributedApplication app,
        ProcessFaultCommandProtocol.Identity expected, CancellationToken token)
    {
        var command = await app.Services.GetRequiredService<ResourceCommandService>().ExecuteCommandAsync(
            expected.ResourceName, ProcessFaultCommandProtocol.CommandName, Arguments(expected), token);
        return ReadResult(command, expected, token);
    }

    internal static InteractionInputCollection Arguments(ProcessFaultCommandProtocol.Identity expected)
        => new([
            Input(ProcessFaultCommandProtocol.ExpectedProcessIdArgument, expected.ProcessId),
            Input(ProcessFaultCommandProtocol.ExpectedAspireStartArgument, expected.AspireStartTimeUtcTicks),
            Input(ProcessFaultCommandProtocol.ExpectedProcessStartArgument, expected.ProcessStartTimeUtcTicks)
        ]);

    private static InteractionInput Input(string name, long value) => new()
    {
        Name = name,
        InputType = InputType.Text,
        Value = value.ToString(CultureInfo.InvariantCulture)
    };

    internal static ProcessFaultCommandProtocol.Result ReadResult(ExecuteCommandResult command,
        ProcessFaultCommandProtocol.Identity expected, CancellationToken token)
    {
        if (command.Canceled)
        {
            token.ThrowIfCancellationRequested();
            throw new OperationCanceledException("The owned Aspire process fault command was canceled.", token);
        }
        var data = command.Data;
        if (!command.Success || data is null || data.Format != CommandResultFormat.Json
            || data.Value.Length > ProcessFaultCommandProtocol.MaximumResultCharacters)
        {
            throw new InvalidOperationException(command.Message ?? ProcessFaultCommandProtocol.Refused);
        }
        var actual = JsonSerializer.Deserialize<ProcessFaultCommandProtocol.Result>(data.Value)
            ?? throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
        if (actual.ResourceName != expected.ResourceName || actual.ProcessId != expected.ProcessId
            || actual.AspireStartTimeUtcTicks != expected.AspireStartTimeUtcTicks
            || actual.ProcessStartTimeUtcTicks != expected.ProcessStartTimeUtcTicks)
        {
            throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
        }
        return actual;
    }
}
