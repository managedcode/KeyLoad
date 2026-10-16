using KeyLoad.Clustering;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ClusterRouting.CommandObservedPolicyProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class CommandObservedPolicyCalls
{
    internal static async Task<GrainOperationReply> ReceiveAsync(CommandObservedPolicyScenario scenario,
        Guid requestId, ReceiveRequest request, CancellationToken token)
    {
        var source = scenario.Fixture.Source;
        var signed = source.Silo.CatalogRequestCodec().CreateCommand(requestId, scenario.Worker.Id,
            OperationKind.Receive, request.RequestId, NativeSerialization.Serialize(request));
        var failures = new List<Exception>();
        GrainOperationReply? reply = null;
        var identity = source.Silo.OpenRequestContext(scenario.Worker, requestId, request.RequestId, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            reply = await source.Silo.ExecuteAsync(requestId, signed, command: true, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        try
        {
            identity.Dispose();
        }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            failures.Add(cleanup);
        }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            failures.Add(cleanup);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw new InvalidOperationException(P.WrongReply);
    }
}
