using System.Collections.Immutable;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int MaximumNativeGraphIntentResources = 4;

    private void ObserveRemoteGraphReadIntent(IKeyValueView view, RemoteGraphIntentV1 intent,
        ReadExecutionBudget work, bool target)
    {
        var values = intent.ResourceLifetimes;
        if (values.IsDefaultOrEmpty || values.Length > MaximumNativeGraphIntentResources)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteGraphProtocol.Invalid);
        }
        if (target)
        {
            RemoteGraphLifetimeAdmission.RequireReceiver(view, intent, Limits);
        }
        else
        {
            var content = intent.Content;
            var local = RemoteGraphLifetimeAdmission.Capture(view, content.SourcePartition,
                content.Graph, content.Edge.From.Collection, Limits, requireOpen: true);
            if (local.Any(value => !values.Contains(value)))
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteGraphProtocol.Invalid);
            }
        }
        ObserveRemoteGraphReadValues(view, values, work);
    }

    private void ObserveRemoteGraphReadValues(IKeyValueView view, ImmutableArray<ResourceLifetimeV1> values,
        ReadExecutionBudget work)
    {
        foreach (var value in values)
        {
            work.Check();
            ResourceOwnerInventoryValidation.Lifetime(value);
            ResourceRetirementStorage.RequireOpen(view, value, Limits);
        }
        work.ObserveAuthenticatedResourceLifetimes(values);
    }
}
