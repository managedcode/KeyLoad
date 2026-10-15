using System.Text.Json;
using KeyLoad.AppHost.Hosting;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Registers an abrupt fault only on explicitly selected, ephemeral RF3 test resources.</summary>
internal static class ContainerKillCommand
{
    private const string DisplayName = "Test SIGKILL";
    private const string CompletedMessage = "The Aspire resource SIGKILL process completed.";
    private const char InspectionSeparator = '|';
    private const string InspectFormat = "{{.Id}}|{{.State.Status}}";
    private const int SuccessfulExit = 0;
    private const int ExpectedFields = 2;
    private const int ArgumentCount = 1;
    private const int IdField = 0;
    private const int StateField = 1;

    internal static void Apply(IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> resource,
        bool ephemeral, int? benchmarkNodeCount = null)
    {
        var runtime = AppHostOptionsRegistration.Get(builder);
        var control = runtime.Control.Value;
        if (!control.ContainerFaults)
        {
            return;
        }
        if (!ephemeral || benchmarkNodeCount.HasValue || control.BenchmarksEnabled || control.TargetSelected
            || control.ComparisonSelectorsPresent || control.ScaleSelected)
        {
            throw new InvalidOperationException(AppHostControlOptionsRegistration.InvalidContainerFaultSelection);
        }
        var boundName = resource.Resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name;
        resource.WithCommand(ContainerFaultCommandProtocol.CommandName, DisplayName,
            context => ExecuteAsync(resource.Resource.Name, boundName, runtime.TestExecution, context),
            new CommandOptions
            {
                Visibility = ResourceCommandVisibility.Api,
                Arguments = [new InteractionInput
                {
                    Name = ContainerFaultCommandProtocol.ExpectedIdArgument,
                    InputType = InputType.Text,
                    Required = true,
                    MaxLength = ContainerFaultCommandProtocol.FullIdCharacters
                }]
            });
    }

    private static async Task<ExecuteCommandResult> ExecuteAsync(string resourceName, string boundName,
        Microsoft.Extensions.Options.IOptions<TestInfrastructure.TestExecutionOptions> execution,
        ExecuteCommandContext context)
    {
        var expected = context.Arguments.GetString(ContainerFaultCommandProtocol.ExpectedIdArgument);
        if (context.Arguments.Count != ArgumentCount
            || expected is null || expected.Length != ContainerFaultCommandProtocol.FullIdCharacters
            || !expected.All(Uri.IsHexDigit))
        {
            return CommandResults.Failure(ContainerFaultCommandProtocol.Refused);
        }
        var inspected = await ContainerFaultProcess.RunAsync(
            [ContainerFaultCommandProtocol.InspectCommand, ContainerFaultCommandProtocol.FormatArgument, InspectFormat, boundName], execution, context.CancellationToken).ConfigureAwait(false);
        var fields = inspected.Output.Trim().Split(InspectionSeparator);
        if (inspected.ExitCode != SuccessfulExit || fields.Length != ExpectedFields
            || fields[IdField] != expected || fields[StateField] != ContainerFaultCommandProtocol.RunningState)
        {
            return CommandResults.Failure(ContainerFaultCommandProtocol.Refused);
        }
        context.CancellationToken.ThrowIfCancellationRequested();
        var actual = await ContainerFaultProcess.RunAsync([ContainerFaultCommandProtocol.KillCommand, ContainerFaultCommandProtocol.SignalArgument,
            ContainerFaultCommandProtocol.KillSignal, expected],
            execution, context.CancellationToken).ConfigureAwait(false);
        var result = new ContainerFaultCommandProtocol.Result(resourceName, boundName, expected,
            actual.ExitCode, actual.Output, actual.Error);
        return new ExecuteCommandResult
        {
            Success = actual.ExitCode == SuccessfulExit,
            Message = CompletedMessage,
            Data = new CommandResultData { Value = JsonSerializer.Serialize(result), Format = CommandResultFormat.Json }
        };
    }
}
