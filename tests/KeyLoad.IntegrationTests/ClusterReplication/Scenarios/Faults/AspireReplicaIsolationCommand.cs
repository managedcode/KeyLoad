using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Translates only original generated rules into typed resource command fields.</summary>
internal static class AspireReplicaIsolationCommand
{
    private const int Operation = 0;
    private const int Chain = 1;
    private const int Peer = 5;
    private const int Port = 6;
    private const int CreateArity = 2;
    private const int AppendArity = 10;
    private const int InsertArity = 5;
    private const int DeleteArity = 4;

    internal static async Task<ContainerRuntimeProcessResult> ExecuteAsync(DistributedApplication app,
        string resource, string containerName, string expectedId, string[] originalRule, CancellationToken token)
    {
        var result = await InvokeAsync(app, resource, expectedId, Decode(originalRule), token).ConfigureAwait(false);
        return AspireContainerFaultCommand.ReadResult(result, resource, containerName, expectedId, token);
    }

    internal static Task<ExecuteCommandResult> InvokeAsync(DistributedApplication app, string resource, string expectedId,
        ReplicaIsolationCommandProtocol.Rule rule, CancellationToken token)
        => app.Services.GetRequiredService<ResourceCommandService>().ExecuteCommandAsync(resource,
            ReplicaIsolationCommandProtocol.CommandName, new InteractionInputCollection(
            [
                new InteractionInput { Name = ContainerFaultCommandProtocol.ExpectedIdArgument, InputType = InputType.Text, Value = expectedId },
                new InteractionInput { Name = ReplicaIsolationCommandProtocol.RuleArgument, InputType = InputType.Text,
                    Value = JsonSerializer.Serialize(rule) }
            ]), token);

    private static ReplicaIsolationCommandProtocol.Rule Decode(string[] rule)
    {
        if (rule.Length < CreateArity)
        {
            throw new InvalidOperationException(ReplicaIsolationCommandProtocol.Refused);
        }
        var action = rule[Operation] switch
        {
            "-N" when rule.Length == CreateArity => ReplicaIsolationCommandProtocol.Action.CreateChain,
            "-A" when rule.Length == AppendArity => ReplicaIsolationCommandProtocol.Action.AppendDrop,
            "-I" when rule.Length == InsertArity => ReplicaIsolationCommandProtocol.Action.InsertJump,
            "-D" when rule.Length == DeleteArity => ReplicaIsolationCommandProtocol.Action.DeleteJump,
            "-F" when rule.Length == CreateArity => ReplicaIsolationCommandProtocol.Action.FlushChain,
            "-X" when rule.Length == CreateArity => ReplicaIsolationCommandProtocol.Action.DeleteChain,
            _ => throw new InvalidOperationException(ReplicaIsolationCommandProtocol.Refused)
        };
        var chain = action is ReplicaIsolationCommandProtocol.Action.InsertJump or ReplicaIsolationCommandProtocol.Action.DeleteJump
            ? rule[^1] : rule[Chain];
        var input = chain.StartsWith(ReplicaIsolationCommandProtocol.InputPrefix, StringComparison.Ordinal);
        var prefix = input ? ReplicaIsolationCommandProtocol.InputPrefix : ReplicaIsolationCommandProtocol.OutputPrefix;
        if (!chain.StartsWith(prefix, StringComparison.Ordinal)
            || chain.Length != prefix.Length + ReplicaIsolationCommandProtocol.SuffixLength)
        {
            throw new InvalidOperationException(ReplicaIsolationCommandProtocol.Refused);
        }
        RequireShape(rule, action, input);
        var append = action == ReplicaIsolationCommandProtocol.Action.AppendDrop;
        return new(action, input, chain[prefix.Length..], append ? rule[Peer] : null,
            append && rule[Port] == ReplicaIsolationProtocol.SourcePort);
    }

    private static void RequireShape(string[] rule, ReplicaIsolationCommandProtocol.Action action, bool input)
    {
        const int ProtocolOption = 2;
        const int ProtocolValue = 3;
        const int Direction = 4;
        const int PortValue = 7;
        const int Jump = 8;
        const int Verdict = 9;
        var valid = action switch
        {
            ReplicaIsolationCommandProtocol.Action.AppendDrop => rule[ProtocolOption] == "-p" && rule[ProtocolValue] == "tcp"
                && rule[Direction] == (input ? "-s" : "-d") && rule[Port] is "--sport" or "--dport"
                && rule[PortValue] == "11111" && rule[Jump] == "-j" && rule[Verdict] == "DROP",
            ReplicaIsolationCommandProtocol.Action.InsertJump => rule[Chain] == (input ? "INPUT" : "OUTPUT")
                && rule[ProtocolOption] == "1" && rule[ProtocolValue] == "-j",
            ReplicaIsolationCommandProtocol.Action.DeleteJump => rule[Chain] == (input ? "INPUT" : "OUTPUT")
                && rule[ProtocolOption] == "-j",
            _ => true
        };
        if (!valid)
        {
            throw new InvalidOperationException(ReplicaIsolationCommandProtocol.Refused);
        }
    }
}
