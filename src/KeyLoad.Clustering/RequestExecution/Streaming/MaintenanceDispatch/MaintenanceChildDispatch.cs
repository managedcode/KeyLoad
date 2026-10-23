using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Joins one execution Task child before restoring its identity and owned payload.</summary>
internal static class MaintenanceChildDispatch
{
    internal static async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest parent, GrainRequestEnvelope child,
        Guid actorId, Guid commandId,
        Func<GrainRequestEnvelope, CancellationToken, Task<GrainOperationReply>> executeChild,
        IServiceProvider services, GrainRequestCodec codec, IResourceLifetimeWorkBinding binding,
        byte[]? ownedPayload, CancellationToken token)
    {
        var admission = new MaintenanceChildDispatchAdmission(ownedPayload);
        var failures = new List<Exception>();
        GrainOperationReply? result = null;
        try
        {
            admission.Open(parent, child, actorId, commandId, services, codec, binding, token);
            await admission.ObserveStartedAsync(codec).ConfigureAwait(true);
            result = await executeChild(child, admission.Token).ConfigureAwait(true);
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        if (failures.Count != NoFailures || result?.Error is not null)
        {
            await admission.CancelAsync(failures).ConfigureAwait(true);
        }
        await admission.JoinObservationAsync(failures).ConfigureAwait(true);
        admission.PrepareCleanup(failures);
        admission.Dispose();
        if (failures.Count != NoFailures && result?.Error is { } original)
        {
            failures.Insert(NoFailures, Errors.Fail(original, result.SafeDetail ?? GrainRoutingProtocol.InvalidRequest));
        }
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
