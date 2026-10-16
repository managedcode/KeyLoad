using KeyLoad.Core;
using KeyLoad.Features.InternalSerialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Materializes only a fresh strict outcome after its actual footprint debits the original allowance.</summary>
internal static class NativeCommandObservationSourceReply
{
    internal static GrainOperationReply Encode(NativeCommandObservationResultV1 observed,
        GrainOperationReply first, IServiceProvider services, ReadExecutionBudget work, CancellationToken token)
    {
        work.CheckResult(observed.Result.NativeValue);
        if (observed.Result.Error is null
            && NativeSerialization.Measure(new GrainValue(observed.Result.NativeValue)) > work.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }
        var options = services.GetRequiredService<IOptions<GrainRoutingOptions>>();
        var reply = GrainReplyFactory.Operation(observed.Result, options, token);
        return reply with
        {
            WorkProof = first.WorkProof
        };
    }
}
