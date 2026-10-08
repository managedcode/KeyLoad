namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Recognizes only the closed movement child modes and retains the original typed issued-operation input.</summary>
internal static class ControlledPartitionMovementProcessScenario
{
    private const int ArgumentCount = 2;
    private const int RootArgument = 0;
    private const int ModeArgument = 1;

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length != ArgumentCount || args[ModeArgument] is not
            (ControlledPartitionMovementProcessProtocol.Prepare or ControlledPartitionMovementProcessProtocol.Fault
            or ControlledPartitionMovementProcessProtocol.Recover or ControlledPartitionMovementProcessProtocol.Verify))
        { return false; }
        var root = Path.GetFullPath(args[RootArgument]);
        var input = await ControlledPartitionMovementProcessFiles.ReadAsync<ControlledPartitionMovementProcessInput>(
            root, ControlledPartitionMovementProcessProtocol.InputFile, CancellationToken.None);
        await ControlledPartitionMovementPhaseChild.RunAsync(root, args[ModeArgument], input, CancellationToken.None);
        return true;
    }
}
