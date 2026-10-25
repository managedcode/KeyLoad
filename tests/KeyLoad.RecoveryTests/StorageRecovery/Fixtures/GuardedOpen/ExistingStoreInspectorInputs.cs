using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.ResourceExecution;

namespace KeyLoad.RecoveryTests;

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

    internal global::ManagedCode.Communication.Result<ZoneTreeStoreOptions?> CreateOptions()
    {
        const int MaxFrameBytesEmptyCount = 0;
        const int MaxSnapshotBytesEmptyCount = 0;

        var options = new ZoneTreeStoreOptions(request.Directory) { Incarnation = request.Incarnation };
        return request.Variant switch
        {
            ExistingStoreInspectionVariant.NullOptions => global::ManagedCode.Communication.Result<ZoneTreeStoreOptions?>.FailNotFound(nameof(CreateOptions)),
            ExistingStoreInspectionVariant.MissingIncarnation => global::ManagedCode.Communication.Result<ZoneTreeStoreOptions?>.Succeed(((ZoneTreeStoreOptions?)(options with { Incarnation = null }))),
            ExistingStoreInspectionVariant.EmptyIncarnation => global::ManagedCode.Communication.Result<ZoneTreeStoreOptions?>.Succeed(((ZoneTreeStoreOptions?)(options with { Incarnation = Guid.Empty }))),
            ExistingStoreInspectionVariant.RelativeDirectory => global::ManagedCode.Communication.Result<ZoneTreeStoreOptions?>.Succeed(((ZoneTreeStoreOptions?)(options with { Directory = RelativeDirectory }))),
            ExistingStoreInspectionVariant.EmptyDirectory => global::ManagedCode.Communication.Result<ZoneTreeStoreOptions?>.Succeed(((ZoneTreeStoreOptions?)(options with { Directory = string.Empty }))),
            ExistingStoreInspectionVariant.NonCanonicalDirectory => global::ManagedCode.Communication.Result<ZoneTreeStoreOptions?>.Succeed(((ZoneTreeStoreOptions?)(options with { Directory = Path.Combine(request.Directory, DotSegment) }))),
            ExistingStoreInspectionVariant.ZeroFrameBudget => global::ManagedCode.Communication.Result<ZoneTreeStoreOptions?>.Succeed(((ZoneTreeStoreOptions?)(options with { MaxFrameBytes = MaxFrameBytesEmptyCount }))),
            ExistingStoreInspectionVariant.ZeroSnapshotBudget => global::ManagedCode.Communication.Result<ZoneTreeStoreOptions?>.Succeed(((ZoneTreeStoreOptions?)(options with { MaxSnapshotBytes = MaxSnapshotBytesEmptyCount }))),
            ExistingStoreInspectionVariant.Cache => global::ManagedCode.Communication.Result<ZoneTreeStoreOptions?>.Succeed(((ZoneTreeStoreOptions?)(ConfigureCache(options)))),
            ExistingStoreInspectionVariant.Observer => global::ManagedCode.Communication.Result<ZoneTreeStoreOptions?>.Succeed(((ZoneTreeStoreOptions?)(options with { FaultObserver = (_, _, _) => ObservedStages++ }))),
            _ => global::ManagedCode.Communication.Result<ZoneTreeStoreOptions?>.Succeed(((ZoneTreeStoreOptions?)(options)))
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
