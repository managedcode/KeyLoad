using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static EntityRef BindRemoteGraphDestination(RemoteGraphSourceMutationV1 original, EntityRef requested)
    {
        var preparations = original.Preparations.Where(value => !value.Preparation.Request.Deleted
            && value.Preparation.Request.Destination == requested).ToArray();
        if (preparations.Length != RemoteGraphSinglePreparation)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid);
        }
        return RemoteGraphLifetimeAdmission.BindDestination(preparations[RemoteGraphFirstPreparation].Preparation);
    }
}
