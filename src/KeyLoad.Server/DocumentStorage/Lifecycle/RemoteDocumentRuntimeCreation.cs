namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteDocumentRuntimeCreation
{
    internal static async Task<RemoteDocumentRuntime?> CreateAsync(OrleansNode node,
        PartitionHost partition, ServerRuntimeOptions options, TimeProvider clock)
    {
        var settings = options.Node.Value.PhysicalGroupTrust;
        if (!settings.RemoteDocumentReads)
        {
            return null;
        }
        var runtime = new RemoteDocumentRuntime(options);
        try
        {
            partition.TransferVerification?.AttachRuntime(runtime);
            runtime.Initialize(node, partition, options, clock);
            return runtime;
        }
        catch (Exception primary)
        {
            await RemoteDocumentRuntimeLifecycle.SettleCreationAsync(runtime, primary).ConfigureAwait(false);
            throw;
        }
    }

}
