using KeyLoad.RecoveryTests.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2RecoveryOwnerReadiness
{
    internal static Task RequireAsync(string ownerRoot, CancellationToken token)
        => ControlledPartitionMovementProcessOwnership.RequireAsync(ownerRoot,
            RecoveryExecutionOptions.NativeProcessReadiness(), TimeProvider.System, token);

    internal static async Task RequirePairAsync(string root, string source, string target, CancellationToken token)
    {
        foreach (var name in new[] { source, target })
        {
            await RequireAsync(Path.Combine(root, name), token).ConfigureAwait(false);
        }
    }
}
