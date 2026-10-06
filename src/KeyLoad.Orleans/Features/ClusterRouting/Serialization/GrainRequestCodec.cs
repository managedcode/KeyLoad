using System.Buffers.Text;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Issues and verifies database-signed authority while preserving bounded exact native payload bytes.</summary>
public sealed partial class GrainRequestCodec
{
    private const int SignatureBytes = 32;
    private const int SeparatorCharacters = 1;
    private readonly DatabaseEngine database;
    private readonly TimeProvider clock;
    private readonly int maximumTokenCharacters;
    private readonly GrainRoutingOptions settings;
    private readonly IOptions<GrainRoutingOptions> executionOptions;

    internal IOptions<GrainRoutingOptions> ExecutionOptions => executionOptions;

    internal IGrainRequestPhaseObserver? PhaseObserver { get; init; }

    internal bool HasPhaseObserver => PhaseObserver is not null;

    /// <summary>Uses the shared durable signing key and incarnation, with the runtime system clock.</summary>
    /// <param name="database">Borrowed canonical database with immutable cluster signing scope.</param>
    /// <param name="clock">System clock for issuance and expiry checks.</param>
    /// <param name="options">The centrally validated signed request and execution lifetimes.</param>
    public GrainRequestCodec(DatabaseEngine database, TimeProvider clock, IOptions<GrainRoutingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(clock);
        this.database = database;
        this.clock = clock;
        ArgumentNullException.ThrowIfNull(options);
        settings = options.Value;
        executionOptions = options;
        var envelopeBytes = checked(database.Limits.MaxBatchBytes + GrainRoutingProtocol.EnvelopeMetadataBytes);
        maximumTokenCharacters = checked(Base64Url.GetEncodedLength(envelopeBytes)
            + GrainNativeContracts.SignedTokenPrefix.Length + SeparatorCharacters + Base64Url.GetEncodedLength(SignatureBytes));
    }

    /// <summary>Signs one GUID read request; API-key authentication alone has no principal identifier.</summary>
    /// <param name="requestId">Unique GUID for both the request actor and its independent read actor.</param>
    /// <param name="principalId">Persisted principal ID, or null for API-key authentication.</param>
    /// <param name="kind">Exactly one defined read capability.</param>
    /// <param name="payload">Exact native typed bytes, including the native zero marker for no-DTO capabilities.</param>
    /// <returns>A bounded database-signed envelope with the current incarnation and expiry.</returns>
    public string CreateRead(Guid requestId, string? principalId, GrainReadKind kind, ReadOnlyMemory<byte> payload)
        => Issue(new GrainRequestEnvelope
        {
            Purpose = GrainNativeContracts.RequestPurpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = principalId,
            ReadKind = kind,
            Payload = Encode(payload),
            ExpiresAt = clock.GetUtcNow() + settings.RequestLifetime
        });

    /// <summary>Signs one independently keyed request without changing the write's durable deduplication ID.</summary>
    /// <param name="requestId">Fresh request actor identity, independent from a stable write retry.</param>
    /// <param name="principalId">Current persisted principal identifier.</param>
    /// <param name="kind">Public operation kind; Membership is forbidden.</param>
    /// <param name="commandId">Stable nonempty command ID reused when resolving an uncertain write.</param>
    /// <param name="payload">Exact bounded native bytes for the typed operation contract.</param>
    /// <returns>A bounded signed request for canonical partition routing.</returns>
    public string CreateCommand(Guid requestId, string principalId, OperationKind kind, Guid commandId, ReadOnlyMemory<byte> payload)
        => Issue(new GrainRequestEnvelope
        {
            Purpose = GrainNativeContracts.RequestPurpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = principalId,
            CommandKind = kind,
            CommandId = commandId,
            Payload = Encode(payload),
            ExpiresAt = clock.GetUtcNow() + settings.RequestLifetime
        });

    internal DecodedGrainRequest Verify(string signedRequest)
    {
        if (string.IsNullOrEmpty(signedRequest) || signedRequest.Length > maximumTokenCharacters)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        var request = database.Verify<GrainRequestEnvelope>(signedRequest, maximumTokenCharacters);
        ValidateScope(request);
        if (request.Payload.Length > database.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, GrainRoutingProtocol.InvalidRequest);
        }

        GrainNativePayload.Validate(request.Payload.Span);
        return new(request, request.Payload);
    }

    internal DecodedGrainRequest VerifyRead(string signedRequest, Guid actorId)
    {
        var request = VerifyRequest(signedRequest, actorId);
        if (request.Envelope.ReadKind is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        return request;
    }

    internal DecodedGrainRequest VerifyRequest(string signedRequest, Guid actorId)
    {
        var request = Verify(signedRequest);
        if (request.Envelope.RequestId != actorId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }

        return request;
    }

    internal ValueTask ObservePhaseAsync(DecodedGrainRequest request, GrainRequestPhase phase,
        IGrainContext? context, CancellationToken cancellationToken)
    {
        var observer = PhaseObserver;
        if (observer is null)
        {
            return ValueTask.CompletedTask;
        }

        ArgumentNullException.ThrowIfNull(context);
        return observer.ObserveAsync(GrainRequestProbeIdentity.From(request.Envelope), phase, context, cancellationToken);
    }

    internal void ObserveProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext? context)
    {
        var observer = PhaseObserver;
        if (observer is null)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(context);
        observer.ProducerDisposed(identity, context);
    }

    private ReadOnlyMemory<byte> Encode(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length > database.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, GrainRoutingProtocol.InvalidRequest);
        }

        GrainNativePayload.Validate(payload.Span);
        return payload.ToArray();
    }

    private string Issue(GrainRequestEnvelope request)
    {
        ValidateScope(request);
        return database.Sign(request);
    }

    internal void ValidateScope(GrainRequestEnvelope request)
        => GrainRequestScope.Validate(request, database.Store.Identity.Incarnation, clock.GetUtcNow(), settings.MaximumFuture);
}

internal readonly record struct DecodedGrainRequest(GrainRequestEnvelope Envelope, ReadOnlyMemory<byte> Payload);
