using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal interface IEventVectorParent
{
    Task<GrainOperationReply> ExecuteAdmitAsync(DecodedGrainRequest original,
        GrainRequestCodec codec, global::Orleans.Runtime.IGrainContext context, ReadExecutionBudget work, CancellationToken originalCancellationToken);
    Task<EventFeedControlResult?> ReadTailOfferAsync(DecodedGrainRequest original, ReadExecutionBudget work,
        CancellationToken originalCancellationToken);
}
