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
    internal const string ExpectedLaunchProcessIdArgument = "expectedLaunchProcessId";
    internal const string ExpectedLaunchProcessStartArgument = "expectedLaunchProcessStartTimeUtcTicks";
    internal const string ProcessIdProperty = "executable.pid";
    internal const int MaximumIdentityCharacters = 20;
    internal const int MaximumResultCharacters = 131_072;
    internal const string Refused = "The selected Aspire project does not own the expected running process.";

    internal sealed record LaunchIdentity(string ResourceName, int ProcessId, long AspireStartTimeUtcTicks,
        long ProcessStartTimeUtcTicks);

    internal sealed record Identity(string ResourceName, int ProcessId, long AspireStartTimeUtcTicks,
        long ProcessStartTimeUtcTicks, int LaunchProcessId, long LaunchProcessStartTimeUtcTicks)
    {
        internal LaunchIdentity Launch => new(ResourceName, LaunchProcessId, AspireStartTimeUtcTicks,
            LaunchProcessStartTimeUtcTicks);
    }

    /// <summary>Describes both joined exits; Unix server exit status is unavailable to this non-parent observer.</summary>
    internal sealed record Result(string ResourceName, int ProcessId, long AspireStartTimeUtcTicks,
        long ProcessStartTimeUtcTicks, int LaunchProcessId, long LaunchProcessStartTimeUtcTicks,
        int? ExitCode, int LaunchExitCode);
}
