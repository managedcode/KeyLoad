using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests;

internal sealed class ExistingStoreInspectorInputs(ExistingStoreInspectionRequest request)
{
    private const string RelativeDirectory = "relative-existing-store";
    private const string DotSegment = ".";
    internal int ObservedStages
    {
        get; private set;
    }
    internal Guid NodeId => request.Variant == ExistingStoreInspectionVariant.EmptyNodeId ? Guid.Empty : request.ExpectedNodeId;

    internal (bool HasValue, ZoneTreeStoreOptions? Value) CreateOptions()
    {
        var options = new ZoneTreeStoreOptions(request.Directory) { Incarnation = request.Incarnation };
        return request.Variant switch
        {
            ExistingStoreInspectionVariant.NullOptions => (false, default(ZoneTreeStoreOptions?)),
            ExistingStoreInspectionVariant.MissingIncarnation => (true, ((ZoneTreeStoreOptions?)(options with { Incarnation = null }))),
            ExistingStoreInspectionVariant.EmptyIncarnation => (true, ((ZoneTreeStoreOptions?)(options with { Incarnation = Guid.Empty }))),
            ExistingStoreInspectionVariant.RelativeDirectory => (true, ((ZoneTreeStoreOptions?)(options with { Directory = RelativeDirectory }))),
            ExistingStoreInspectionVariant.EmptyDirectory => (true, ((ZoneTreeStoreOptions?)(options with { Directory = string.Empty }))),
            ExistingStoreInspectionVariant.NonCanonicalDirectory => (true, ((ZoneTreeStoreOptions?)(options with { Directory = Path.Combine(request.Directory, DotSegment) }))),
            ExistingStoreInspectionVariant.Observer => (true, ((ZoneTreeStoreOptions?)(options with { FaultObserver = (_, _, _) => ObservedStages++ }))),
            _ => (true, ((ZoneTreeStoreOptions?)(options)))
        };
    }

}
