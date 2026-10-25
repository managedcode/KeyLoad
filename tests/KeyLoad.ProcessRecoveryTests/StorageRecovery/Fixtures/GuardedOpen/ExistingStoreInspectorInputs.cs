using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.ResourceExecution;

namespace KeyLoad.ProcessRecoveryTests;

internal sealed class ExistingStoreInspectorInputs(ExistingStoreInspectionRequest request)
{
    private const string RelativeDirectory = "relative-existing-store";
    private const string DotSegment = ".";
    internal CacheMemoryBudget? Budget
    {
        get; private set;
    }
    internal int ObservedStages
    {
        get; private set;
    }
    internal Guid NodeId => request.Variant == ExistingStoreInspectionVariant.EmptyNodeId ? Guid.Empty : request.ExpectedNodeId;

    internal (bool HasValue, ZoneTreeStoreOptions? Value) CreateOptions()
    {
        const int MaxFrameBytesEmptyCount = 0;
        const int MaxSnapshotBytesEmptyCount = 0;

        var options = new ZoneTreeStoreOptions(request.Directory) { Incarnation = request.Incarnation };
        return request.Variant switch
        {
            ExistingStoreInspectionVariant.NullOptions => (false, default(ZoneTreeStoreOptions?)),
            ExistingStoreInspectionVariant.MissingIncarnation => (true, ((ZoneTreeStoreOptions?)(options with { Incarnation = null }))),
            ExistingStoreInspectionVariant.EmptyIncarnation => (true, ((ZoneTreeStoreOptions?)(options with { Incarnation = Guid.Empty }))),
            ExistingStoreInspectionVariant.RelativeDirectory => (true, ((ZoneTreeStoreOptions?)(options with { Directory = RelativeDirectory }))),
            ExistingStoreInspectionVariant.EmptyDirectory => (true, ((ZoneTreeStoreOptions?)(options with { Directory = string.Empty }))),
            ExistingStoreInspectionVariant.NonCanonicalDirectory => (true, ((ZoneTreeStoreOptions?)(options with { Directory = Path.Combine(request.Directory, DotSegment) }))),
            ExistingStoreInspectionVariant.ZeroFrameBudget => (true, ((ZoneTreeStoreOptions?)(options with { MaxFrameBytes = MaxFrameBytesEmptyCount }))),
            ExistingStoreInspectionVariant.ZeroSnapshotBudget => (true, ((ZoneTreeStoreOptions?)(options with { MaxSnapshotBytes = MaxSnapshotBytesEmptyCount }))),
            ExistingStoreInspectionVariant.Cache => (true, ((ZoneTreeStoreOptions?)(ConfigureCache(options)))),
            ExistingStoreInspectionVariant.Observer => (true, ((ZoneTreeStoreOptions?)(options with { FaultObserver = (_, _, _) => ObservedStages++ }))),
            _ => (true, ((ZoneTreeStoreOptions?)(options)))
        };
    }

    private ZoneTreeStoreOptions ConfigureCache(ZoneTreeStoreOptions options)
    {
        Budget = new(CrashExecutionOptions.CacheMemory());
        return options with
        {
            EmbeddedPointCache = new ZoneTreePointCacheOptions(Budget)
        };
    }
}
