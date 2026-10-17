using System.Text.Json;
using KeyLoad.AppHost.Hosting;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Owns native namespace mutations only on the already admitted diagnostic resource cohort.</summary>
internal static class ReplicaIsolationCommand
{
    private const string DisplayName = "Test namespace isolation rule";
    private const string CompletedMessage = "The AppHost namespace rule process completed.";
    private const string NetworksKey = "Networks";
    private const string CapabilityKey = "CapAdd";
    private const string StateKey = "State";
    private const string PrivilegedKey = "Privileged";
    private const string PidModeKey = "PidMode";
    private const string NameKey = "Name";
    private const string ConfiguredImageKey = "ConfigImage";
    private const string RuntimeUserKey = "User";
    private const string FaultSourceKey = "FaultSource";
    private const string IncarnationKey = "Incarnation";
    private const string NetworkIdKey = "NetworkID";
    private const string NetworkModeKey = "NetworkMode";
    private const string IpAddressKey = "IPAddress";
    private const string IdKey = "Id";
    private const string NativeNetAdmin = "CAP_NET_ADMIN";
    private const string NativeNamePrefix = "/";
    private const string IncarnationFormat = "D";
    private const int ArgumentCount = 2;
    private const int One = 1;
    private const int First = 0;
    private const int Success = 0;
    private const string Inspection = """{"Id":{{json .Id}},"Name":{{json .Name}},"ConfigImage":{{json .Config.Image}},"User":{{json .Config.User}},"State":{{json .State.Status}},"CapAdd":{{json .HostConfig.CapAdd}},"Privileged":{{json .HostConfig.Privileged}},"NetworkMode":{{json .HostConfig.NetworkMode}},"PidMode":{{json .HostConfig.PidMode}},"Networks":{{json .NetworkSettings.Networks}},"Incarnation":{{json (index .Config.Labels "keyload.fault.incarnation")}},"FaultSource":{{json (index .Config.Labels "keyload.fault.source")}}}""";

    internal static void Apply(IDistributedApplicationBuilder builder, ReplicaIsolationBuildPlan plan)
    {
        var runtime = AppHostOptionsRegistration.Get(builder);
        if (!runtime.Control.Value.ContainerFaults
            || !runtime.Control.Value.Ephemeral || runtime.Control.Value.BenchmarksEnabled
            || runtime.Control.Value.TargetSelected || runtime.Control.Value.ComparisonSelectorsPresent)
        {
            throw new InvalidOperationException(ReplicaIsolationCommandProtocol.Refused);
        }
        var nodes = builder.Resources.OfType<ContainerResource>()
            .Where(node => plan.Targets.Any(target => target.ResourceName == node.Name))
            .ToDictionary(node => node.Name, node => node.Annotations.OfType<ContainerNameAnnotation>().Single().Name,
                StringComparer.Ordinal);
        foreach (var target in plan.Targets)
        {
            var resource = builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>()
                .Single(node => node.Name == target.ResourceName));
            resource.WithCommand(ReplicaIsolationCommandProtocol.CommandName, DisplayName,
                context => ExecuteAsync(plan, nodes, target, runtime.TestExecution, context),
                new CommandOptions
                {
                    Visibility = ResourceCommandVisibility.Api,
                    Arguments =
                    [
                        new InteractionInput { Name = ContainerFaultCommandProtocol.ExpectedIdArgument, InputType = InputType.Text,
                            Required = true, MaxLength = ContainerFaultCommandProtocol.FullIdCharacters },
                        new InteractionInput { Name = ReplicaIsolationCommandProtocol.RuleArgument, InputType = InputType.Text,
                            Required = true, MaxLength = ReplicaIsolationCommandProtocol.MaximumRuleCharacters }
                    ]
                });
        }
    }

    private static async Task<ExecuteCommandResult> ExecuteAsync(ReplicaIsolationBuildPlan plan,
        Dictionary<string, string> names, ReplicaIsolationBuildTarget target,
        Microsoft.Extensions.Options.IOptions<TestInfrastructure.TestExecutionOptions> execution, ExecuteCommandContext context)
    {
        var expected = context.Arguments.GetString(ContainerFaultCommandProtocol.ExpectedIdArgument);
        var rule = ReplicaIsolationCommandRule.Read(context.Arguments.GetString(ReplicaIsolationCommandProtocol.RuleArgument));
        if (context.Arguments.Count != ArgumentCount || expected is null || rule is null
            || expected.Length != ContainerFaultCommandProtocol.FullIdCharacters
            || !expected.All(character => character is >= ContainerFaultCommandProtocol.DecimalDigitFirst and <= ContainerFaultCommandProtocol.DecimalDigitLast or >= ContainerFaultCommandProtocol.LowerHexDigitFirst and <= ContainerFaultCommandProtocol.LowerHexDigitLast))
        {
            return CommandResults.Failure(ReplicaIsolationCommandProtocol.Refused);
        }
        var peers = new HashSet<string>(StringComparer.Ordinal);
        string? network = null;
        foreach (var member in plan.Targets)
        {
            var inspection = await ContainerFaultProcess.RunAsync([ContainerFaultCommandProtocol.InspectCommand, ContainerFaultCommandProtocol.FormatArgument, Inspection, names[member.ResourceName]],
                execution, context.CancellationToken).ConfigureAwait(false);
            if (inspection.ExitCode != Success)
            {
                return CommandResults.Failure(ReplicaIsolationCommandProtocol.Refused);
            }
            var admitted = ReadAdmitted(inspection.Output, names[member.ResourceName], plan, member);
            if (admitted is null || network is not null && network != admitted.Network
                || member.ResourceName == target.ResourceName && admitted.Id != expected)
            {
                return CommandResults.Failure(ReplicaIsolationCommandProtocol.Refused);
            }
            network = admitted.Network;
            if (member.ResourceName != target.ResourceName && !peers.Add(admitted.Ip))
            {
                return CommandResults.Failure(ReplicaIsolationCommandProtocol.Refused);
            }
        }
        var arguments = ReplicaIsolationCommandRule.Build(rule, peers);
        if (arguments is null)
        {
            return CommandResults.Failure(ReplicaIsolationCommandProtocol.Refused);
        }
        context.CancellationToken.ThrowIfCancellationRequested();
        var actual = await ContainerFaultProcess.RunAsync([ReplicaIsolationCommandProtocol.ExecCommand, ReplicaIsolationCommandProtocol.UserArgument, ReplicaIsolationCommandProtocol.RootUser, expected, ReplicaIsolationCommandProtocol.TimeoutExecutable,
            ReplicaIsolationCommandProtocol.TermSignal, ReplicaIsolationCommandProtocol.KillBound, ReplicaIsolationCommandProtocol.ExecutionBound, ReplicaIsolationCommandProtocol.IptablesExecutable, ReplicaIsolationCommandProtocol.WaitArgument, ReplicaIsolationCommandProtocol.WaitBound, .. arguments],
            execution, context.CancellationToken).ConfigureAwait(false);
        var result = new ContainerFaultCommandProtocol.Result(target.ResourceName, names[target.ResourceName], expected,
            actual.ExitCode, actual.Output, actual.Error);
        return new ExecuteCommandResult
        {
            Success = actual.ExitCode == Success,
            Message = CompletedMessage,
            Data = new CommandResultData { Value = JsonSerializer.Serialize(result), Format = CommandResultFormat.Json }
        };
    }

    private static Admitted? ReadAdmitted(string json, string name, ReplicaIsolationBuildPlan plan,
        ReplicaIsolationBuildTarget target)
    {
        using var document = JsonDocument.Parse(json);
        var value = document.RootElement;
        var networks = value.GetProperty(NetworksKey).EnumerateObject().ToArray();
        var capabilities = value.GetProperty(CapabilityKey).EnumerateArray().ToArray();
        if (value.GetProperty(StateKey).GetString() != ContainerFaultCommandProtocol.RunningState || value.GetProperty(PrivilegedKey).GetBoolean()
            || !string.IsNullOrEmpty(value.GetProperty(PidModeKey).GetString()) || networks.Length != One
            || capabilities.Length != One || capabilities[First].GetString() != NativeNetAdmin
            || value.GetProperty(NameKey).GetString() != NativeNamePrefix + name
            || value.GetProperty(ConfiguredImageKey).GetString() != target.ImageReference
            || value.GetProperty(RuntimeUserKey).GetString() != target.ServiceUser
            || value.GetProperty(FaultSourceKey).GetString() != plan.DockerfileSha256
            || value.GetProperty(IncarnationKey).GetString() != plan.Incarnation.ToString(IncarnationFormat))
        {
            return null;
        }
        var network = networks[First];
        var id = network.Value.GetProperty(NetworkIdKey).GetString();
        var mode = value.GetProperty(NetworkModeKey).GetString();
        var ip = network.Value.GetProperty(IpAddressKey).GetString();
        var container = value.GetProperty(IdKey).GetString();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(network.Name) || mode != network.Name && mode != id
            || ip is null || !ReplicaIsolationCommandRule.IsCanonicalPeer(ip)
            || container is null || container.Length != ContainerFaultCommandProtocol.FullIdCharacters
            || !container.All(character => character is >= ContainerFaultCommandProtocol.DecimalDigitFirst and <= ContainerFaultCommandProtocol.DecimalDigitLast or >= ContainerFaultCommandProtocol.LowerHexDigitFirst and <= ContainerFaultCommandProtocol.LowerHexDigitLast))
        {
            return null;
        }
        return new(container, ip, id);
    }

    private sealed record Admitted(string Id, string Ip, string Network);
}
