using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void ObserveRemoteGraphPreparations(IKeyValueView view, RemoteGraphSourceMutationV1 command)
    {
        foreach (var preparation in command.Preparations)
        {
            RemoteGraphLifetimeAdmission.RequirePreparation(preparation.Preparation);
            foreach (var lifetime in preparation.Preparation.ResourceLifetimes)
            {
                ObserveAuthenticatedLifetime(view, lifetime);
            }
        }
    }

    private static void ObserveAuthenticatedLifetime(IKeyValueView view, ResourceLifetimeV1? lifetime)
    {
        if (lifetime is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Unbound);
        }
        ResourceOwnerInventoryValidation.Lifetime(lifetime);
        if (view is IResourceEffectObservation observation)
        {
            observation.ObserveLifetime(lifetime);
        }
    }
}
