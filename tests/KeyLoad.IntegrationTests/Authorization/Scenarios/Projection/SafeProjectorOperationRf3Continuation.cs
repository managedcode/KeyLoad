using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3Continuation
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, CancellationToken token)
    {
        await SafeProjectorOperationRf3Mutations.PatchAsync(root, reader, history, false, token);
        await SafeProjectorOperationRf3Changes.WaveAsync(root, reader, history, false, token);
        await SafeProjectorOperationRf3Authority.SetAsync(root, history, SafeProjectorOperationRf3Protocol.RawReadEpoch,
            [SafeProjectorOperationRf3Protocol.ReadGrant], token);
        await SafeProjectorOperationRf3Initial.WaveAsync(root, reader, history, true, false, token);
        await SafeProjectorOperationRf3Changes.WaveAsync(root, reader, history, true, token);
        await SafeProjectorOperationRf3Mutations.ProtectedRefusedAsync(root, reader, history,
            SafeProjectorOperationRf3Protocol.RawReadEpoch, token);
        await SafeProjectorOperationRf3Authority.SetAsync(root, history, SafeProjectorOperationRf3Protocol.FullWriteEpoch,
            [SafeProjectorOperationRf3Protocol.ReadGrant, SafeProjectorOperationRf3Protocol.UseGrant,
                SafeProjectorOperationRf3Protocol.WriteGrant], token);
        await SafeProjectorOperationRf3Initial.WaveAsync(root, reader, history, true, true, token);
        await SafeProjectorOperationRf3Mutations.PatchAsync(root, reader, history, true, token);
        await SafeProjectorOperationRf3Changes.WaveAsync(root, reader, history, true, token);
        await SafeProjectorOperationRf3Authority.SetAsync(root, history, SafeProjectorOperationRf3Protocol.FinalMaskedEpoch,
            [SafeProjectorOperationRf3Protocol.WriteGrant], token);
        await FinalAsync(root, reader, history, token);
        await SafeProjectorOperationRf3Mutations.RoundtripAsync(root, reader, history,
            SafeProjectorOperationRf3Protocol.FinalMaskedEpoch, token);
        await SafeProjectorOperationRf3Replay.AllAsync(root, reader, history, SafeProjectorOperationRf3Protocol.FinalMaskedEpoch, token);
    }

    internal static async Task FinalAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, CancellationToken token)
    {
        await SafeProjectorOperationRf3Initial.WaveAsync(root, reader, history, false, false, token);
        await SafeProjectorOperationRf3Changes.WaveAsync(root, reader, history, false, token);
    }
}
