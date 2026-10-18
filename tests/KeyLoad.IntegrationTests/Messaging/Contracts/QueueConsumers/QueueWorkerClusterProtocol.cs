using KeyLoad.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerClusterProtocol
{
    internal const string HandlerFailure = "Caller-owned worker handler failed.";
    internal const string Tenant = "external-worker-cluster-";
    internal const string Database = "worker-db";
    internal const string Domain = "worker-domain";
    internal const string Queue = "worker-queue";
    internal const string First = "first";
    internal const string Second = "second";
    internal const string Third = "third";
    internal const string Healthy = "healthy";
    internal const string Payload = "{\"job\":1}";
    internal const string Headers = "{\"origin\":\"worker\"}";
    internal const string Enqueue = "enqueue";
    internal const string DataDirectory = "data";
    internal const string Missing = "The native worker predecessor was not established.";
    internal const int NoDeliveries = 0;
    internal const int NoAcknowledgements = 0;
    internal const int NoLeaseVersion = 0;
    internal const int NoAttempts = 0;
    internal const int NoActiveHandlers = 0;
    internal const int PositivePositionBoundary = 0;
    internal const int FirstLaneIndex = 0;
    internal const int SecondLaneIndex = 1;
    internal const int SingleDelivery = 1;
    internal const int ConcurrentHandlerLimit = 2;
    internal const int InvocationDeliveries = 3;
    internal const int RemainingReadyDeliveries = 2;
    internal const int ExpectedJoinedHandlers = 1;
    internal const int ReadyStateVersion = 1;
    internal const int ClaimStateVersion = 2;
    internal const int AckStateVersion = 3;
    internal const int RenewedStateVersion = 3;
    internal const int FirstReadySequence = 1;
    internal const int SecondReadySequence = 2;
    internal const int ThirdReadySequence = 3;
    internal const int HealthyReadySequence = 4;
    internal const int ReadySequenceOffset = 1;
    internal const int ClaimAttempt = 1;
    internal const int InitialLeaseVersion = 1;
    internal const int InitialDeliveryGeneration = 1;
    internal const int PlacementSchemaVersion = 1;
    internal const int PolicyEpochIncrement = 1;
    internal const int SingleReceiveBudget = 1;
    internal const int NoActiveBytes = 0;
    internal const int SingleRenewalBudget = 1;
    internal const int RenewalProbePolicyEpoch = 3;
    internal const int RenewedAckStateVersion = 4;
    internal const int RenewalCadenceSeconds = 1;
    internal const int LeaseSeconds = 300;
    internal const int RenewalSeconds = 299;
    internal const Capability Worker = Capability.QueuePublish | Capability.QueueConsume
        | Capability.QueueAck | Capability.QueueRenew | Capability.QueueInspect;
    internal static readonly string[] Messages = [First, Second, Third];

    internal static IOptions<QueueWorkerOptions> Options(int deliveries)
        => Microsoft.Extensions.Options.Options.Create(new QueueWorkerOptions
        {
            MaximumConcurrentHandlers = ConcurrentHandlerLimit,
            MaximumDeliveries = deliveries,
            LeaseSeconds = LeaseSeconds,
            RenewalInterval = TimeSpan.FromSeconds(RenewalSeconds)
        });
}
