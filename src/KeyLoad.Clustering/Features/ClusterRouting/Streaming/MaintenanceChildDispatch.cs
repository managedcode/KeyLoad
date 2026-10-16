using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

/// <summary>Joins one originally reserved native child without interpreting its business reply.</summary>
internal static class MaintenanceChildDispatch
{
    internal static async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest parent, string signed,
        Guid actorId, Guid commandId,
        Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> executeChild,
        IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        IOptions<GrainRoutingOptions> routing, IResourceLifetimeWorkBinding binding,
        byte[]? ownedPayload, CancellationToken token)
    {
        var admission = new MaintenanceChildDispatchAdmission(ownedPayload);
        var failures = new List<Exception>();
        GrainOperationReply? result = null;
        try
        {
            admission.Open(parent, signed, actorId, commandId, services, codec, clock, binding, token);
            result = await GrainRequestStreamConsumer.DrainAsync(cancellation => executeChild(signed, cancellation),
                serializer, actorId, clock, routing, admission.Token).ConfigureAwait(true);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        await admission.CompleteAsync(failures).ConfigureAwait(true);
        ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
    }

    private static void ThrowIfAny(List<Exception> failures)
    {
        var fatal = failures.Select(CqrsRuntimeFailures.FindFatal).FirstOrDefault(value => value is not null);
        if (fatal is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fatal).Throw();
        }
        if (failures.Count != NoFailures)
        {
            throw failures.Count == OneFailure ? failures[NoFailures] : new AggregateException(failures);
        }
    }

    private const int NoFailures = 0;
    private const int OneFailure = 1;
}
