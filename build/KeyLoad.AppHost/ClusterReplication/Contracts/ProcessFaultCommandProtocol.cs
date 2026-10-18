namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Binds an abrupt test fault to the actual Aspire project process and both startup identities.</summary>
internal static class ProcessFaultCommandProtocol
{
    internal const string EnabledSetting = "KeyLoadTests:ProcessFaults";
    internal const string EnabledArgument = "--KeyLoadTests:ProcessFaults=true";
    internal const string CommandName = "keyload-test-process-kill";
    internal const string ExpectedProcessIdArgument = "expectedProcessId";
    internal const string ExpectedAspireStartArgument = "expectedAspireStartTimeUtcTicks";
    internal const string ExpectedProcessStartArgument = "expectedProcessStartTimeUtcTicks";
    internal const string ProcessIdProperty = "executable.pid";
    internal const int MaximumIdentityCharacters = 20;
    internal const int MaximumResultCharacters = 131_072;
    internal const string Refused = "The selected Aspire project does not own the expected running process.";

    internal sealed record Identity(string ResourceName, int ProcessId, long AspireStartTimeUtcTicks,
        long ProcessStartTimeUtcTicks);

    /// <summary>Describes the process that was deliberately killed and whose exit was joined.</summary>
    internal sealed record Result(string ResourceName, int ProcessId, long AspireStartTimeUtcTicks,
        long ProcessStartTimeUtcTicks, int ExitCode);
}
