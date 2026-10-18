using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationClusterContinuation
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, CancellationToken token)
    {
        await SafeProjectorOperationClusterMutations.PatchAsync(root, reader, history, false, token);
        await SafeProjectorOperationClusterChanges.WaveAsync(root, reader, history, false, token);
        await SafeProjectorOperationClusterAuthority.SetAsync(root, history, SafeProjectorOperationClusterProtocol.RawReadEpoch,
            [SafeProjectorOperationClusterProtocol.ReadGrant], token);
        await SafeProjectorOperationClusterInitial.WaveAsync(root, reader, history, true, false, token);
        await SafeProjectorOperationClusterChanges.WaveAsync(root, reader, history, true, token);
        await SafeProjectorOperationClusterMutations.ProtectedRefusedAsync(root, reader, history,
            SafeProjectorOperationClusterProtocol.RawReadEpoch, token);
        await SafeProjectorOperationClusterAuthority.SetAsync(root, history, SafeProjectorOperationClusterProtocol.FullWriteEpoch,
            [SafeProjectorOperationClusterProtocol.ReadGrant, SafeProjectorOperationClusterProtocol.UseGrant,
                SafeProjectorOperationClusterProtocol.WriteGrant], token);
        await SafeProjectorOperationClusterInitial.WaveAsync(root, reader, history, true, true, token);
        await SafeProjectorOperationClusterMutations.PatchAsync(root, reader, history, true, token);
        await SafeProjectorOperationClusterChanges.WaveAsync(root, reader, history, true, token);
        await SafeProjectorOperationClusterAuthority.SetAsync(root, history, SafeProjectorOperationClusterProtocol.FinalMaskedEpoch,
            [SafeProjectorOperationClusterProtocol.WriteGrant], token);
        await FinalAsync(root, reader, history, token);
        await SafeProjectorOperationClusterMutations.RoundtripAsync(root, reader, history,
            SafeProjectorOperationClusterProtocol.FinalMaskedEpoch, token);
        await SafeProjectorOperationClusterReplay.AllAsync(root, reader, history, SafeProjectorOperationClusterProtocol.FinalMaskedEpoch, token);
    }

    internal static async Task FinalAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, CancellationToken token)
    {
        await SafeProjectorOperationClusterInitial.WaveAsync(root, reader, history, false, false, token);
        await SafeProjectorOperationClusterChanges.WaveAsync(root, reader, history, false, token);
    }
}
