namespace KeyLoad.Server;

/// <summary>Named process interruption boundaries in a stopped physical-node conversion.</summary>
internal enum NodeFormatUpgradeStage
{
    SourceVerified,
    StoresConverted,
    ImagesConverted,
    DescriptorFlushed,
    TargetVerified,
    Published
}
