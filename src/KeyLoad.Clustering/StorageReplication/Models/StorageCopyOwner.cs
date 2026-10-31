namespace KeyLoad.Clustering;

/// <summary>One actual reachable physical copy and its most recently confirmed canonical cut.</summary>
internal readonly record struct StorageCopyOwner(SiloAddress Address, string Name, SiloStorageCut Cut);
