using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class RequestCqrsFatalObservation
{
    private readonly List<CqrsStreamChunkKind> observedKinds = [];
    private readonly List<Guid?> observedRequestIds = [];
    private int sequence;
    private int producerSettlementCount;
    private int activationSettlementCount;
    private int producerSettlementOrder;
    private int activationSettlementOrder;

    internal int ProducerSettlementCount => Volatile.Read(ref producerSettlementCount);
    internal int ActivationSettlementCount => Volatile.Read(ref activationSettlementCount);
    internal int ProducerSettlementOrder => Volatile.Read(ref producerSettlementOrder);
    internal int ActivationSettlementOrder => Volatile.Read(ref activationSettlementOrder);
    internal int ObservedChunkCount => observedKinds.Count;

    internal CqrsStreamChunkKind ObservedChunkKind(int index) => observedKinds[index];
    internal Guid? ObservedRequestId(int index) => observedRequestIds[index];

    internal void MarkChunkObserved(CqrsStreamChunkKind kind, Guid? requestId)
    {
        observedKinds.Add(kind);
        observedRequestIds.Add(requestId);
    }

    internal void MarkProducerSettled()
    {
        Interlocked.Increment(ref producerSettlementCount);
        Interlocked.Exchange(ref producerSettlementOrder, Interlocked.Increment(ref sequence));
    }

    internal void MarkActivationSettled()
    {
        Interlocked.Increment(ref activationSettlementCount);
        Interlocked.Exchange(ref activationSettlementOrder, Interlocked.Increment(ref sequence));
    }
}
