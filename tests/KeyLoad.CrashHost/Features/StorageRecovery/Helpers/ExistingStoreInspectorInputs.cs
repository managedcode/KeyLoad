using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.CrashHost;

internal sealed class ExistingStoreInspectorInputs(ExistingStoreInspectionRequest request)
{
    private const string RelativeDirectory = "relative-existing-store";
    private const string DotSegment = ".";
    internal CacheMemoryBudget? Budget { get; private set; }
    internal int ObservedStages { get; private set; }
    internal Guid NodeId => request.Variant == ExistingStoreInspectionVariant.EmptyNodeId ? Guid.Empty : request.ExpectedNodeId;

    internal ZoneTreeStoreOptions? CreateOptions()
    {
        const int MaxFrameBytesEmptyCount = 0;
        const int MaxSnapshotBytesEmptyCount = 0;

        var options = new ZoneTreeStoreOptions(request.Directory) { Incarnation = request.Incarnation };
        return request.Variant switch
        {
            ExistingStoreInspectionVariant.NullOptions => null,
            ExistingStoreInspectionVariant.MissingIncarnation => options with { Incarnation = null },
            ExistingStoreInspectionVariant.EmptyIncarnation => options with { Incarnation = Guid.Empty },
            ExistingStoreInspectionVariant.RelativeDirectory => options with { Directory = RelativeDirectory },
            ExistingStoreInspectionVariant.EmptyDirectory => options with { Directory = string.Empty },
            ExistingStoreInspectionVariant.NonCanonicalDirectory => options with { Directory = Path.Combine(request.Directory, DotSegment) },
            ExistingStoreInspectionVariant.ZeroFrameBudget => options with { MaxFrameBytes = MaxFrameBytesEmptyCount },
            ExistingStoreInspectionVariant.ZeroSnapshotBudget => options with { MaxSnapshotBytes = MaxSnapshotBytesEmptyCount },
            ExistingStoreInspectionVariant.Cache => ConfigureCache(options),
            ExistingStoreInspectionVariant.Observer => options with { FaultObserver = (_, _, _) => ObservedStages++ },
            _ => options
        };
    }

    private ZoneTreeStoreOptions ConfigureCache(ZoneTreeStoreOptions options)
    {
        Budget = new(CrashExecutionOptions.CacheMemory());
        return options with { EmbeddedPointCache = new ZoneTreePointCacheOptions(Budget) };
    }
}
