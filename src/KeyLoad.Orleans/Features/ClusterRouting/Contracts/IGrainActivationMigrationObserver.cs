namespace KeyLoad.Orleans;

/// <summary>Optional private native migration observation; never database effect authority.</summary>
/// <remarks>Context is borrowed only until the actual callback returns.</remarks>
internal interface IGrainActivationMigrationObserver
{
    ValueTask<SiloAddress?> PrepareMigrationAsync(GrainRequestProbeIdentity identity,
        IGrainContext context, CancellationToken cancellationToken);
}
