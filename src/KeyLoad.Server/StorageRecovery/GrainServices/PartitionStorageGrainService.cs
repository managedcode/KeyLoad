using OpenTelemetry;

namespace KeyLoad.Server;

/// <summary>Opens this silo's one physical storage owner in the awaited native initialization phase.</summary>
/// <param name="grainId">The native service identity supplied by Orleans registration.</param>
/// <param name="silo">The actual owning silo, independent of logical partition activation placement.</param>
/// <param name="loggerFactory">The borrowed native logger factory.</param>
/// <param name="owner">The same silo-local owner retained through opening and eventual joined physical close.</param>
internal sealed class PartitionStorageGrainService(GrainId grainId, Silo silo, ILoggerFactory loggerFactory,
    PartitionHostApplicationOwner owner) : GrainService(grainId, silo, loggerFactory), IPartitionStorageGrainService
{
    private readonly PartitionHostApplicationOwner owner = owner;

    /// <inheritdoc />
    public override Task Init(IServiceProvider serviceProvider)
    {
        serviceProvider.GetRequiredService<ITelemetryHostInitializer>().Initialize();
        return owner.OpenAsync();
    }
}
