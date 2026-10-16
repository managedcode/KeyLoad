using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

internal static class AnnMaintenanceChildDispatch
{
    private const int NoFailures = 0;
    private const int OneFailure = 1;

    internal static async Task<TResult> ExecuteAsync<TResult>(DecodedGrainRequest parent,
        Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> executeChild,
        IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        IOptions<GrainRoutingOptions> routing, ILogger diagnostics, IResourceLifetimeWorkBinding binding,
        byte[] payload, GrainReadKind? read, OperationKind? command, Guid commandId, CancellationToken token) where TResult : class
    {
        var failures = new List<Exception>();
        var cleanup = new List<Exception>();
        NativeRequestWorkChildReservation? reservation = null;
        CancellationTokenSource? cancellation = null;
        GrainRequestIdentityScope? identity = null;
        TResult? result = null;
        try
        {
            token.ThrowIfCancellationRequested();
            codec.ValidateScope(parent.Envelope);
            GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
            var database = services.GetRequiredService<DatabaseEngine>();
            var principal = GrainRequestAuthority.ReloadForRequest(database, parent.Envelope, clock);
            GrainRequestAuthority.RequireAdministrator(principal);
            var actorId = Guid.NewGuid();
            var signed = codec.CreateAnnMaintenanceChild(actorId, principal.Id, read, command, commandId, payload, parent.Envelope.ExpiresAt);
            var verified = codec.VerifyRequest(signed, actorId);
            var producer = RequireParent(binding);
            reservation = producer.ReserveLocalChild(verified, services.GetRequiredService<PhysicalShardRecord>(),
                services.GetRequiredService<ReplicaSiloDiscoveryState>().Read());
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, reservation.Cancellation);
            identity = new(services, principal, actorId, commandId, cancellation.Token,
                connectionId: NativeConnectionExecutionIdentity.Resolve(services));
            result = await ReadAsync<TResult>(executeChild, signed, serializer, actorId, parent.Envelope.RequestId,
                clock, routing, diagnostics, command, cancellation.Token).ConfigureAwait(true);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        var original = failures.Count == NoFailures ? null : failures[NoFailures];
        Capture(() => identity?.Dispose(), cleanup);
        Capture(() => cancellation?.Dispose(), cleanup);
        Capture(() => CryptographicOperations.ZeroMemory(payload), cleanup);
        failures.AddRange(cleanup);
        if (reservation is not null)
        {
            try
            {
                _ = await reservation.CompleteDispatchAsync(original, cleanup.ToImmutableArray()).ConfigureAwait(true);
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, AnnMaintenanceProtocol.Interrupted);
    }

    private static async Task<TResult> ReadAsync<TResult>(
        Func<string, CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> execute,
        string signed, Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid actorId, Guid parentId, TimeProvider clock, IOptions<GrainRoutingOptions> routing,
        ILogger diagnostics, OperationKind? command, CancellationToken token) where TResult : class
    {
        var invoked = false;
        GrainOperationReply reply;
        try
        {
            reply = await GrainRequestStreamConsumer.DrainAsync(cancellation =>
            {
                invoked = true;
                return execute(signed, cancellation);
            }, serializer, actorId, clock, routing, token).ConfigureAwait(true);
        }
        catch (KeyLoadException error) when (invoked && command is not null)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parentId,
                GrainFailureStage.CapabilityExecution, ErrorCode.UnknownWriteOutcome);
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, AnnMaintenanceProtocol.Interrupted);
        }
        if (reply.Error is { } failure)
        {
            throw Errors.Fail(failure, reply.SafeDetail ?? GrainRoutingProtocol.InvalidRequest);
        }
        try
        {
            return NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as TResult
                ?? throw Errors.Fail(command is null ? ErrorCode.Corruption : ErrorCode.UnknownWriteOutcome,
                    AnnMaintenanceProtocol.Interrupted);
        }
        catch (Exception error) when (command is not null && error is KeyLoadException or ArgumentException)
        {
            GrainFailureDiagnostics.Log(diagnostics, error, parentId,
                GrainFailureStage.ReplyEncoding, ErrorCode.UnknownWriteOutcome);
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, AnnMaintenanceProtocol.Interrupted);
        }
    }

    private static NativeRequestWorkLease RequireParent(IResourceLifetimeWorkBinding binding)
    {
        if (binding is not INativeRequestWorkBinding native
            || !ReferenceEquals(binding.ResultRetention, native.OriginalProducer.ResultRetention))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        return native.OriginalProducer;
    }

    private static void Capture(Action operation, List<Exception> failures)
    {
        try
        {
            operation();
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
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
}
