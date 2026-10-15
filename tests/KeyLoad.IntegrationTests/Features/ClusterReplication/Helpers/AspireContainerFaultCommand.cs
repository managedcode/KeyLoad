using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Invokes the command on the original Aspire resource and retains its actual engine result.</summary>
internal static class AspireContainerFaultCommand
{
    internal static async Task<ContainerRuntimeProcessResult> KillAsync(DistributedApplication app,
        string resourceName, string containerName, string expectedId, CancellationToken token)
    {
        var command = await app.Services.GetRequiredService<ResourceCommandService>().ExecuteCommandAsync(
            resourceName, ContainerFaultCommandProtocol.CommandName,
            new InteractionInputCollection([new InteractionInput
            {
                Name = ContainerFaultCommandProtocol.ExpectedIdArgument,
                InputType = InputType.Text,
                Value = expectedId
            }]), token);
        return ReadResult(command, resourceName, containerName, expectedId, token);
    }

    internal static ContainerRuntimeProcessResult ReadResult(ExecuteCommandResult command, string resourceName,
        string containerName, string expectedId, CancellationToken token)
    {
        if (command.Canceled)
        {
            token.ThrowIfCancellationRequested();
            throw new OperationCanceledException("The owned Aspire fault command was canceled.", token);
        }
        var data = command.Data;
        if (data is null || data.Format != CommandResultFormat.Json
            || data.Value.Length > ContainerFaultCommandProtocol.MaximumResultCharacters)
        {
            throw new InvalidOperationException(command.Message ?? ContainerFaultCommandProtocol.Refused);
        }
        var actual = JsonSerializer.Deserialize<ContainerFaultCommandProtocol.Result>(data.Value)
            ?? throw new InvalidOperationException(ContainerFaultCommandProtocol.Refused);
        if (actual.ResourceName != resourceName || actual.ContainerName != containerName
            || actual.ContainerId != expectedId || command.Success != (actual.ExitCode == ContainerRuntimeProtocol.SuccessfulExitCode))
        {
            throw new InvalidOperationException(ContainerFaultCommandProtocol.Refused);
        }
        return new(actual.ExitCode, actual.StandardOutput, actual.StandardError);
    }
}
