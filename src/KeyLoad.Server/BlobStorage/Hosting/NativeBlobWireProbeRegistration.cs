namespace KeyLoad.Server.Features.BlobStorage;

internal static class NativeBlobWireProbeRegistration
{
    internal static void Register(IServiceCollection services, NodeOptions options)
    {
        var selected = options.NativeBlobWireProbe;
        if (selected.Root is null && selected.SessionId is null)
        {
            return;
        }
        services.AddSingleton(provider => NativeBlobWireProbeFactory.Create(provider, provider.GetRequiredService<PartitionHostApplicationOwner>().Borrow(), options)
            ?? throw new InvalidOperationException(NativeBlobWireProbeProtocol.InvalidOwner));
        services.AddSingleton<Func<INativeControlledBlobBorrowObserver>>(provider =>
        {
            var borrowed = provider.GetRequiredService<NativeBlobWireProbeObserver>();
            return () => borrowed;
        });
    }
}
