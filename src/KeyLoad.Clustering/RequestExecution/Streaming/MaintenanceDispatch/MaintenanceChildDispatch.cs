using KeyLoad.Core.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

/// <summary>Joins one execution Task child before restoring its identity.</summary>
internal static class MaintenanceChildDispatch
{
    internal static async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest parent, GrainRequestEnvelope child,
        Guid actorId, Guid commandId,
        Func<GrainRequestEnvelope, CancellationToken, Task<GrainOperationReply>> executeChild,
        IServiceProvider services, GrainRequestCodec codec, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        var admission = new MaintenanceChildDispatchAdmission();
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

    /// <summary>Issues one child under the parent's current scope, joins it and returns its typed result.</summary>
    internal static async Task<TResult> CallAsync<TResult>(DecodedGrainRequest parent,
        Func<Guid, string, GrainRequestEnvelope> issue, Guid commandId,
        Func<GrainRequestEnvelope, CancellationToken, Task<GrainOperationReply>> executeChild,
        IServiceProvider services, GrainRequestCodec codec, ILogger diagnostics, IResourceLifetimeWorkBinding binding,
        string interrupted, CancellationToken token) where TResult : class
    {
        token.ThrowIfCancellationRequested();
        codec.ValidateScope(parent.Envelope);
        GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
        var actorId = Guid.NewGuid();
        var child = issue(actorId, parent.Envelope.PrincipalId!);
        var command = commandId != Guid.Empty;
        var invoked = false;
        GrainOperationReply reply;
        try
        {
            reply = await ExecuteAsync(parent, child, actorId, commandId, (value, cancellation) =>
            {
                invoked = true;
                return executeChild(value, cancellation);
            }, services, codec, binding, token).ConfigureAwait(true);
        }
        catch (KeyLoadException error) when (invoked && command)
        {
            throw Interrupted(diagnostics, error, parent, GrainFailureStage.CapabilityExecution, interrupted);
        }
        if (reply.Error is { } failure)
        {
            throw Errors.Fail(failure, reply.SafeDetail ?? GrainRoutingProtocol.InvalidRequest);
        }
        try
        {
            return GrainReplyFactory.RequireValue(reply).Value as TResult
                ?? throw Errors.Fail(command ? ErrorCode.UnknownWriteOutcome : ErrorCode.Corruption, interrupted);
        }
        catch (Exception error) when (command && error is KeyLoadException or ArgumentException)
        {
            throw Interrupted(diagnostics, error, parent, GrainFailureStage.ReplyEncoding, interrupted);
        }
    }

    /// <summary>Runs a parent flow and always releases its maintenance session afterwards.</summary>
    internal static async Task<TResult> RunThenAbortAsync<TResult>(Func<Task<TResult>> flow, Func<Task> abort)
    {
        TResult result;
        try
        {
            result = await flow().ConfigureAwait(true);
        }
        catch (Exception primary)
        {
            try
            {
                await abort().ConfigureAwait(true);
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        await abort().ConfigureAwait(true);
        return result;
    }

    private static KeyLoadException Interrupted(ILogger diagnostics, Exception error, DecodedGrainRequest parent,
        GrainFailureStage stage, string interrupted)
    {
        GrainFailureDiagnostics.Log(diagnostics, error, parent.Envelope.RequestId, stage, ErrorCode.UnknownWriteOutcome);
        return Errors.Fail(ErrorCode.UnknownWriteOutcome, interrupted);
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
