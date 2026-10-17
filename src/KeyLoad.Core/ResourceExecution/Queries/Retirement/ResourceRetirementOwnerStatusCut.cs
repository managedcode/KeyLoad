using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementOwnerStatusCutV1 CaptureRetirementStatusCut(IKeyValueView view)
    {
        var catalog = ReadInventoryPhysicalCatalog(view);
        long applied = ResourceRetirementProtocol.Empty;
        if (!view.ReadValue(KeySpace.AppliedBytes, bytes => applied = NativeSerialization.Deserialize<long>(bytes))
            || applied < ResourceRetirementProtocol.Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var identity = Store.Identity;
        return new(identity.NodeId, identity.Incarnation, identity.ReadGeneration, Store.Position, applied, catalog.Revision);
    }
}
