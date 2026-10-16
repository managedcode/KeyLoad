using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.Clustering;

/// <summary>Retains the fixed original read reservation and exact signed admission until target settlement.</summary>
internal sealed class NativeCommandObservationSourceScope
{
    private const int MetadataHeadroomDivisor = 2;
    private const int MinimumAllowance = 1;
    private const string Uncertain = "The write outcome is unknown. Retry the same command ID.";
    private const string CauseKey = "keyload.native-command-observation-source-failure";
    private readonly ReadExecutionBudgetReadGrant grant;
    private readonly NativeCommandObservationAdmissionV1 admission;

    private NativeCommandObservationSourceScope(DecodedGrainRequest request, string signed,
        ReadExecutionBudgetReadGrant grant, NativeCommandObservationAdmissionV1 admission)
    {
        Request = request;
        Signed = signed;
        this.grant = grant;
        this.admission = admission;
    }

    internal DecodedGrainRequest Request
    {
        get;
    }
    internal string Signed
    {
        get;
    }
    internal bool Dispatched
    {
        get; set;
    }
    internal bool TargetSettled
    {
        get; set;
    }
    private string? opaqueToken;

    internal static NativeCommandObservationSourceScope Create(DecodedGrainRequest request,
        GrainRequestCodec codec, ReadExecutionBudget work)
    {
        work.Check();
        var bytes = work.RemainingReadGrantBytes / MetadataHeadroomDivisor;
        var records = work.RemainingReadGrantRecords / MetadataHeadroomDivisor;
        var maximumResultBytes = work.MaximumResultBytes;
        if (bytes < MinimumAllowance || records < MinimumAllowance || maximumResultBytes < MinimumAllowance)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }
        var admission = new NativeCommandObservationAdmissionV1(NativeCommandObservationProtocol.CurrentVersion,
            Guid.NewGuid(), bytes, records, maximumResultBytes);
        var signed = codec.AttachCommandObservation(request, admission);
        var grant = work.CreateReadGrant(bytes, records);
        return new(new(request.Envelope with
        {
            Observation = admission
        }, request.Payload), signed, grant, admission);
    }

    internal string AcceptClaim(GrainOperationReply prepared, ReadExecutionBudget work)
    {
        opaqueToken = RequireToken(prepared, work);
        return opaqueToken;
    }

    internal void RequireBusinessProof(GrainOperationReply business, ReadExecutionBudget work)
    {
        if (!string.Equals(RequireToken(business, work), opaqueToken, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
    }

    internal string RequireToken(GrainOperationReply first, ReadExecutionBudget work)
    {
        var proof = NativeCommandResultWorkBinding.RequireProof(Request, first);
        work.CheckResult(proof);
        if (NativeSerialization.Measure(first) > work.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }
        if (!first.Payload.IsEmpty || proof.ObservationToken is not { Length: > 0 } opaque)
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
        return opaque;
    }

    internal NativeCommandObservationResultV1 Accept(GrainOperationReply reply, ReadExecutionBudget work)
    {
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? GrainRoutingProtocol.InvalidRequest);
        }
        if (reply.Payload.IsEmpty || reply.Payload.Length > admission.MaximumResultBytes
            || reply.Payload.Length > work.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
        var observed = GrainNativePayload.Read<NativeCommandObservationResultV1>(reply.Payload);
        if (observed.Result is null || observed.Lifetimes.IsDefault || observed.ReadBytes < 0
            || observed.ExaminedRecords < 0 || observed.ReadBytes > admission.MaximumReadBytes
            || observed.ExaminedRecords > admission.MaximumExaminedRecords)
        {
            throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        }
        work.ImportSettledReadGrant(grant, observed.ReadBytes, observed.ExaminedRecords);
        return observed;
    }

    internal async Task<Exception?> CompleteAsync(INativeCommandObservationTransport transport,
        ReadExecutionBudget work, Exception? primary)
    {
        Exception? cleanup = null;
        if (opaqueToken is not null && !TargetSettled)
        {
            cleanup = await NativeRequestWorkSettlement.JoinAsync(
                AbortAsync(transport)).ConfigureAwait(true);
        }
        try
        {
            work.CompleteSettledReadGrant(grant);
        }
        catch (Exception error)
        {
            cleanup = NativeRequestWorkSettlement.Preserve(cleanup, error);
        }
        if (Dispatched && primary is not null && NativeCqrsBoundaryErrors.IsNonFatal(primary))
        {
            var unknown = Errors.Fail(ErrorCode.UnknownWriteOutcome, Uncertain);
            unknown.Data[CauseKey] = primary;
            primary = unknown;
        }
        return NativeRequestWorkSettlement.Preserve(primary, cleanup);
    }

    private async Task AbortAsync(INativeCommandObservationTransport transport)
    {
        await transport.AbortAsync(opaqueToken!, CancellationToken.None).ConfigureAwait(true);
    }
}
