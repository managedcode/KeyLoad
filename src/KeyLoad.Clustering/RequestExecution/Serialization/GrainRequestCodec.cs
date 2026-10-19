using KeyLoad.Core.Features.InternalSerialization;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Issues and verifies database-signed authority while preserving bounded exact execution payload bytes.</summary>
public sealed partial class GrainRequestCodec
{
    private const int SigningKeyBytes = 32;
    private readonly byte[] signingKey;
    private readonly TimeProvider clock;
    private readonly int maximumTokenCharacters;
    private readonly GrainRoutingOptions settings;
    private readonly IOptions<GrainRoutingOptions> executionOptions;

    internal IOptions<GrainRoutingOptions> ExecutionOptions => executionOptions;

    internal Guid Incarnation { get; }
    internal IOptions<DatabaseLimits> OperationLimits { get; }
    internal DatabaseLimits Limits { get; }
    internal TimeProvider RuntimeClock => clock;

    internal (GrainRequestCodec Scope, DateTimeOffset Now, TimeSpan Lifetime) LifetimeReadContext
        => (this, clock.GetUtcNow(), settings.RequestLifetime);

    internal IGrainRequestPhaseObserver? PhaseObserver
    {
        get; init;
    }

    internal bool HasPhaseObserver => PhaseObserver is not null;

    internal IGrainRequestPurposeObserver? PurposeObserver
    {
        get; init;
    }

    /// <summary>Uses the shared durable signing key and incarnation, with the runtime system clock.</summary>
    /// <param name="incarnation">Configured immutable database incarnation.</param>
    /// <param name="signingKey">Trusted database signing credential, copied into this issuer.</param>
    /// <param name="limits">Centrally validated execution payload and operation limits.</param>
    /// <param name="clock">System clock for issuance and expiry checks.</param>
    /// <param name="options">The centrally validated signed request and execution lifetimes.</param>
    public GrainRequestCodec(Guid incarnation, ReadOnlyMemory<byte> signingKey, IOptions<DatabaseLimits> limits,
        TimeProvider clock, IOptions<GrainRoutingOptions> options)
    {
        if (incarnation == Guid.Empty || signingKey.Length != SigningKeyBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
        }
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(clock);
        Incarnation = incarnation;
        this.signingKey = signingKey.ToArray();
        OperationLimits = limits;
        Limits = limits.Value;
        Limits.Validate();
        this.clock = clock;
        ArgumentNullException.ThrowIfNull(options);
        settings = options.Value;
        executionOptions = options;
        maximumTokenCharacters = GrainRequestTokenBounds.MaximumCharacters(Limits.MaxBatchBytes);
    }

    /// <summary>Signs one GUID read request; API-key authentication alone has no principal identifier.</summary>
    /// <param name="requestId">Unique GUID for both the request actor and its independent read actor.</param>
    /// <param name="principalId">Persisted principal ID, or null for API-key authentication.</param>
    /// <param name="kind">Exactly one defined read capability.</param>
    /// <param name="payload">Exact execution typed bytes, including the execution zero marker for no-DTO capabilities.</param>
    /// <returns>A bounded database-signed envelope with the current incarnation and expiry.</returns>
    public string CreateRead(Guid requestId, string? principalId, GrainReadKind kind, ReadOnlyMemory<byte> payload)
        => Issue(GrainRequestEnvelopeConstruction.Read(requestId, Incarnation,
            principalId, kind, Encode(payload), clock.GetUtcNow() + settings.RequestLifetime));

    /// <summary>Signs one independently keyed request without changing the write's durable deduplication ID.</summary>
    /// <param name="requestId">Fresh request actor identity, independent from a stable write retry.</param>
    /// <param name="principalId">Current persisted principal identifier.</param>
    /// <param name="kind">Defined public operation kind; private execution operations require their dedicated scope.</param>
    /// <param name="commandId">Stable nonempty command ID reused when resolving an uncertain write.</param>
    /// <param name="payload">Exact bounded execution bytes for the typed operation contract.</param>
    /// <returns>A bounded signed request for canonical partition routing.</returns>
    public string CreateCommand(Guid requestId, string principalId, OperationKind kind, Guid commandId, ReadOnlyMemory<byte> payload)
        => Issue(GrainRequestEnvelopeConstruction.Command(requestId, Incarnation,
            principalId, kind, commandId, Encode(payload), clock.GetUtcNow() + settings.RequestLifetime));

    internal DecodedGrainRequest Verify(string signedRequest)
        => GrainRequestPayloadVerification.Verify(this, maximumTokenCharacters, signedRequest);

    internal string Sign<T>(T claims) => CoreClaims.Sign(signingKey, claims);

    internal T VerifySigned<T>(string token, int maximumCharacters)
        => CoreClaims.Verify<T>(signingKey, token, maximumCharacters);

    internal DecodedGrainRequest VerifyRead(string signedRequest, Guid actorId)
        => GrainRequestPayloadVerification.VerifyRead(this, signedRequest, actorId);

    internal DecodedGrainRequest VerifyRequest(string signedRequest, Guid actorId)
        => GrainRequestPayloadVerification.VerifyRequest(Verify(signedRequest), actorId);

    internal ValueTask ObservePhaseAsync(DecodedGrainRequest request, GrainRequestPhase phase,
        IGrainContext? context, CancellationToken cancellationToken)
        => GrainRequestPhaseObservation.ObserveAsync(PhaseObserver, request, phase, context, cancellationToken);

    internal ValueTask RuntimeObservePhaseAsync(DecodedGrainRequest request, GrainRequestPhase phase,
        SiloStorageIdentityProvider actualRuntime, CancellationToken cancellationToken)
        => GrainRequestPhaseObservation.RuntimeObserveAsync(PhaseObserver, request, phase, actualRuntime, cancellationToken);

    internal ValueTask ObserveProducerDisposedAsync(GrainRequestProbeIdentity identity, Guid connectionId, IGrainContext? context)
        => GrainRequestPhaseObservation.ProducerDisposedAsync(PhaseObserver, identity, connectionId, context);

    internal void ObserveProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext? context)
        => GrainRequestPhaseObservation.ProducerDisposed(PhaseObserver, identity, context);

    private ReadOnlyMemory<byte> Encode(ReadOnlyMemory<byte> payload)
        => GrainPayload.Copy(payload, Limits.MaxBatchBytes);

    internal string Issue(GrainRequestEnvelope request)
    {
        ValidateScope(request);
        return Sign(GrainRequestPurposeObservation.BeforeSign(PurposeObserver, request));
    }

    internal void ValidateScope(GrainRequestEnvelope request)
        => ValidateScope(request, null);

    internal void ValidateVerifiedScope(GrainRequestEnvelope request)
        => ValidateScope(request, PurposeObserver);

    private void ValidateScope(GrainRequestEnvelope request, IGrainRequestPurposeObserver? observer)
        => GrainRequestPurposeObservation.Validate(request,
            new GrainRequestScopeCut(Incarnation, clock.GetUtcNow(), settings.MaximumFuture), observer);
}
