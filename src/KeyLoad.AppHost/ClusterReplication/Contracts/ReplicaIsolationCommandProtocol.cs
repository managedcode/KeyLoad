using System.Text.Json.Serialization;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Closed, bounded fields for the existing diagnostic namespace fault command.</summary>
internal static class ReplicaIsolationCommandProtocol
{
    internal const string CommandName = "keyload-test-namespace-rule";
    internal const string RuleArgument = "rule";
    internal const int MaximumRuleCharacters = 512;
    internal const int MaximumRuleBytes = 512;
    internal const int SuffixLength = 20;
    internal const string InputPrefix = "KL21I_";
    internal const string OutputPrefix = "KL21O_";
    internal const string ExecCommand = "exec";
    internal const string UserArgument = "--user";
    internal const string RootUser = "0";
    internal const string TimeoutExecutable = "/usr/bin/timeout";
    internal const string TermSignal = "--signal=TERM";
    internal const string KillBound = "--kill-after=1s";
    internal const string ExecutionBound = "5s";
    internal const string IptablesExecutable = "/usr/sbin/iptables";
    internal const string WaitArgument = "-w";
    internal const string WaitBound = "4";
    internal const string Refused = "The namespace fault command is outside its admitted resource or rule scope.";

    /// <summary>Only original installation and restoration operations are executable.</summary>
    internal enum Action
    {
        CreateChain,
        AppendDrop,
        InsertJump,
        DeleteJump,
        FlushChain,
        DeleteChain
    }

    /// <summary>Executable paths, command flags, ports and namespaces are AppHost-owned constants.</summary>
    internal sealed record Rule([property: JsonRequired] Action Action, [property: JsonRequired] bool Input,
        [property: JsonRequired] string ChainSuffix, string? PeerAddress = null, bool SourcePort = false);
}
