namespace KeyLoad.Server;

/// <summary>Borrows the existing node request dependencies for one call.</summary>
internal readonly record struct NativeNodeRequestTarget(IGrainFactory? Factory, IServiceProvider? Runtime,
    PhysicalShardCatalogStartup? Catalog, bool DatabaseReady);
