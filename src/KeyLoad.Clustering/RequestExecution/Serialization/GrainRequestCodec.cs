using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Issues and decodes typed internal request envelopes; Orleans transport trust replaces per-hop signing.</summary>
public sealed partial class GrainRequestCodec
{
    private readonly TimeProvider clock;
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

    /// <summary>Creates the envelope issuer for one database incarnation with centrally validated limits.</summary>
    /// <param name="incarnation">The database incarnation every envelope is scoped to.</param>
    /// <param name="limits">Central database operation limits.</param>
    /// <param name="clock">The clock that stamps and checks envelope expiry.</param>
    /// <param name="options">Request lifetime and execution bounds.</param>
    public GrainRequestCodec(Guid incarnation, IOptions<DatabaseLimits> limits, TimeProvider clock,
        IOptions<GrainRoutingOptions> options)
    {
        if (incarnation == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
        }
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        Incarnation = incarnation;
        OperationLimits = limits;
        Limits = limits.Value;
        Limits.Validate();
        this.clock = clock;
        settings = options.Value;
        executionOptions = options;
    }

    /// <summary>Issues a read envelope for one capability.</summary>
    /// <param name="requestId">The caller's request identity.</param>
    /// <param name="principalId">The verified principal, or null for authentication.</param>
    /// <param name="kind">The read capability.</param>
    /// <param name="payload">The exact serialized request payload.</param>
    /// <returns>The scoped envelope.</returns>
    public GrainRequestEnvelope CreateRead(Guid requestId, string? principalId, GrainReadKind kind, ReadOnlyMemory<byte> payload)
        => Issue(GrainRequestEnvelopeConstruction.Read(requestId, Incarnation,
            principalId, kind, Encode(payload), clock.GetUtcNow() + settings.RequestLifetime));

    /// <summary>Issues a command envelope with its stable command identity.</summary>
    /// <param name="requestId">The caller's request identity.</param>
    /// <param name="principalId">The verified principal.</param>
    /// <param name="kind">The command kind.</param>
    /// <param name="commandId">The stable command identity used for replay.</param>
    /// <param name="payload">The exact serialized command payload.</param>
    /// <returns>The scoped envelope.</returns>
    public GrainRequestEnvelope CreateCommand(Guid requestId, string principalId, OperationKind kind, Guid commandId, ReadOnlyMemory<byte> payload)
        => Issue(GrainRequestEnvelopeConstruction.Command(requestId, Incarnation,
            principalId, kind, commandId, Encode(payload), clock.GetUtcNow() + settings.RequestLifetime));

    internal DecodedGrainRequest Decode(GrainRequestEnvelope request)
        => GrainRequestPayloadVerification.Decode(this, request);

    internal DecodedGrainRequest DecodeRead(GrainRequestEnvelope request, Guid actorId)
        => GrainRequestPayloadVerification.DecodeRead(this, request, actorId);

    internal DecodedGrainRequest DecodeRequest(GrainRequestEnvelope request, Guid actorId)
        => GrainRequestPayloadVerification.VerifyRequest(Decode(request), actorId);

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

    internal GrainRequestEnvelope Issue(GrainRequestEnvelope request)
    {
        ValidateScope(request);
        return GrainRequestPurposeObservation.BeforeIssue(PurposeObserver, request);
    }

    internal void ValidateScope(GrainRequestEnvelope request)
        => ValidateScope(request, null);

    internal void ValidateVerifiedScope(GrainRequestEnvelope request)
        => ValidateScope(request, PurposeObserver);

    private void ValidateScope(GrainRequestEnvelope request, IGrainRequestPurposeObserver? observer)
        => GrainRequestPurposeObservation.Validate(request,
            new GrainRequestScopeCut(Incarnation, clock.GetUtcNow(), settings.MaximumFuture), observer);
}
