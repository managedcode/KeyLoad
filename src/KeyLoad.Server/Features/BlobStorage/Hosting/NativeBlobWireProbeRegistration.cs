namespace KeyLoad.Server.Features.BlobStorage;

internal static class NativeBlobWireProbeRegistration
{
    internal static void Register(IServiceCollection services, PartitionHost partition, NodeOptions options)
    {
        var selected = options.NativeBlobWireProbe;
        if (selected.Root is null && selected.SessionId is null)
        {
            return;
        }
        services.AddSingleton(provider => NativeBlobWireProbeFactory.Create(provider, partition, options)
            ?? throw new InvalidOperationException(NativeBlobWireProbeProtocol.InvalidOwner));
        services.AddSingleton<INativeControlledBlobBorrowObserver>(provider => provider.GetRequiredService<NativeBlobWireProbeObserver>());
    }
}
