namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Bounded test-only Aspire fault command contract; values describe the actual engine invocation.</summary>
internal static class ContainerFaultCommandProtocol
{
    internal const string EnabledSetting = "KeyLoadTests:ContainerFaults";
    internal const string EnabledArgument = "--KeyLoadTests:ContainerFaults=true";
    internal const string CommandName = "keyload-test-sigkill";
    internal const string ExpectedIdArgument = "expectedContainerId";
    internal const int FullIdCharacters = 64;
    internal const int MaximumResultCharacters = 131_072;
    internal const string KillCommand = "kill";
    internal const string SignalArgument = "--signal";
    internal const string KillSignal = "KILL";
    internal const string InspectCommand = "inspect";
    internal const string FormatArgument = "--format";
    internal const string RunningState = "running";
    internal const char DecimalDigitFirst = '0';
    internal const char DecimalDigitLast = '9';
    internal const char LowerHexDigitFirst = 'a';
    internal const char LowerHexDigitLast = 'f';
    internal const string Refused = "The selected Aspire resource does not own the expected running container.";

    /// <summary>The actual original SIGKILL process result, bound to the selected resource and inspected identity.</summary>
    internal sealed record Result(string ResourceName, string ContainerName, string ContainerId,
        int ExitCode, string StandardOutput, string StandardError);
}
