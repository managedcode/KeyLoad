using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Encodes actual known receiver metadata costs under the same signed private bounds.</summary>
internal static class NativeCommandMetadataReplies
{
    private const int NoBytes = 0;

    internal static GrainOperationReply Encode(DecodedGrainRequest request, OperationResult result,
        ReadExecutionBudget work, IOptions<GrainRoutingOptions> options)
    {
        try
        {
            return EncodeKnown(request, result, work, options);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            GrainFailureDiagnostics.Mark(error, GrainFailureStage.ReplyEncoding);
            throw;
        }
    }

    private static GrainOperationReply EncodeKnown(DecodedGrainRequest request, OperationResult result,
        ReadExecutionBudget work, IOptions<GrainRoutingOptions> options)
    {
        work.Check();
        if (result.Error == ErrorCode.UnknownWriteOutcome)
        {
            return GrainReplyFactory.Operation(result, options, work.Cancellation);
        }
        if (result.Error is null)
        {
            work.CheckResult(result.NativeValue);
            using var counter = new ResultByteCounterStream(Math.Min(work.MaximumResultBytes,
                options.Value.MaximumReplyBytes), work.Check);
            NativeSerialization.Serialize(new GrainValue(result.NativeValue), counter);
        }
        var reply = GrainReplyFactory.Operation(result, options, work.Cancellation);
        return Attach(request, reply, work, options);
    }

    internal static GrainOperationReply Attach(DecodedGrainRequest request, GrainOperationReply reply,
        ReadExecutionBudget work, IOptions<GrainRoutingOptions> options)
    {
        try
        {
            work.Check();
            var counted = reply with
            {
                Metadata = new(work.ReadBytes, work.ConstrainedNativeRecords, reply.Payload.Length)
            };
            Require(counted, request.Envelope.Metadata!, options);
            if (NativeSerialization.Measure(counted) > work.MaximumResultBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
            }
            work.Check();
            return counted;
        }
        catch (Exception original) when (NativeCqrsBoundaryErrors.IsNonFatal(original))
        {
            ClearAndThrow(reply, original);
            throw;
        }
        catch (Exception original) when (!NativeCqrsBoundaryErrors.IsNonFatal(original))
        {
            ClearAndThrow(reply, original);
            throw;
        }
    }

    private static void ClearAndThrow(GrainOperationReply reply, Exception original)
    {
        var cleanup = NativeRequestWorkSettlement.Observe(() => NativeCommandObservationEncoding.Clear(reply));
        var combined = cleanup is null ? original : new AggregateException(original, cleanup);
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(combined).Throw();
    }

    internal static void Require(GrainOperationReply reply, NativeCommandMetadataAdmissionV1 admission,
        IOptions<GrainRoutingOptions> options)
    {
        var value = reply.Metadata;
        if (!NativeCommandMetadataAdmission.Valid(admission) || value is null || reply.WorkProof is not null
            || value.ReadBytes < NoBytes || value.ReadBytes > admission.MaximumReadBytes
            || value.ExaminedRecords < NoBytes || value.ExaminedRecords > admission.MaximumExaminedRecords
            || value.ResultBytes != reply.Payload.Length || reply.Payload.Length > admission.MaximumResultBytes
            || NativeSerialization.Measure(reply) > Math.Min(admission.MaximumResultBytes, options.Value.MaximumReplyBytes)
            || reply.Error is { } error && (!Enum.IsDefined(error) || error == ErrorCode.UnknownWriteOutcome
                || !reply.Payload.IsEmpty || string.IsNullOrEmpty(reply.SafeDetail)
                || reply.SafeDetail.Length > options.Value.MaximumDetailCharacters)
            || reply.Error is null && (reply.Payload.IsEmpty || reply.SafeDetail is not null))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementReadScope.InvalidRequest);
        }
    }

    internal static Result<GrainOperationReply> StreamResult(DecodedGrainRequest request,
        GrainOperationReply reply, IOptions<GrainRoutingOptions> options)
    {
        if (reply.Metadata is null)
        {
            return GrainReplyFactory.StreamResult(reply, options);
        }
        if (!ResourceRetirementCommandScope.Validate(request.Envelope))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementReadScope.InvalidRequest);
        }
        Require(reply, request.Envelope.Metadata!, options);
        return Result<GrainOperationReply>.Succeed(reply);
    }
}
