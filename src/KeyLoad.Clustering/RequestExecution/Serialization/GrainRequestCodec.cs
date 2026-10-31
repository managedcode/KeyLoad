using KeyLoad.InternalSerialization;

namespace KeyLoad.Clustering;

/// <summary>Issues and decodes typed internal request envelopes; Orleans transport trust replaces per-hop signing.</summary>
public sealed partial class GrainRequestCodec
{
    private readonly TimeProvider clock;

    internal Guid Incarnation { get; }
    internal TimeProvider RuntimeClock => clock;

    internal IGrainRequestPhaseObserver? PhaseObserver
    {
        get; init;
    }

    internal bool HasPhaseObserver => PhaseObserver is not null;

    internal IGrainRequestPurposeObserver? PurposeObserver
    {
        get; init;
    }

    /// <summary>Creates the envelope issuer for one database incarnation.</summary>
    /// <param name="incarnation">The database incarnation every envelope is scoped to.</param>
    /// <param name="clock">The operation clock of this node.</param>
    public GrainRequestCodec(Guid incarnation, TimeProvider clock)
    {
        if (incarnation == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
        }
        ArgumentNullException.ThrowIfNull(clock);
        Incarnation = incarnation;
        this.clock = clock;
    }

    /// <summary>Issues a read envelope for one capability.</summary>
    /// <param name="requestId">The caller's request identity.</param>
    /// <param name="principalId">The verified principal, or null for authentication.</param>
    /// <param name="kind">The read capability.</param>
    /// <param name="payload">The typed request input.</param>
    /// <returns>The scoped envelope.</returns>
    public GrainRequestEnvelope CreateRead(Guid requestId, string? principalId, GrainReadKind kind, object payload)
        => Issue(GrainRequestEnvelopeConstruction.Read(requestId, Incarnation,
            principalId, kind, GrainPayload.Own(payload, ReadProfile(kind))));

    /// <summary>Issues a command envelope with its stable command identity.</summary>
    /// <param name="requestId">The caller's request identity.</param>
    /// <param name="principalId">The verified principal.</param>
    /// <param name="kind">The command kind.</param>
    /// <param name="commandId">The stable command identity used for replay.</param>
    /// <param name="payload">The typed command input.</param>
    /// <returns>The scoped envelope.</returns>
    public GrainRequestEnvelope CreateCommand(Guid requestId, string principalId, OperationKind kind, Guid commandId, object payload)
        => Issue(GrainRequestEnvelopeConstruction.Command(requestId, Incarnation,
            principalId, kind, commandId, GrainPayload.Copy(payload)));

    /// <summary>Public graph and query inputs keep their null elements for owner validation; other reads are strict.</summary>
    internal static ValidationProfile ReadProfile(GrainReadKind kind)
        => kind is GrainReadKind.AstQuery or GrainReadKind.LiveQueryStart or GrainReadKind.LiveQueryRead
            or GrainReadKind.Traverse or GrainReadKind.GraphShortestPath or GrainReadKind.GraphIncomingEdges
            or GrainReadKind.SqlGraphPath or GrainReadKind.PartitionQuery
            ? ValidationProfile.PublicInputElements : ValidationProfile.Strict;

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
            new GrainRequestScopeCut(Incarnation), observer);
}
