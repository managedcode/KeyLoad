namespace KeyLoad.IntegrationTests.Messaging;

internal sealed class SubscriptionIndependentCheckpointR52State(SubscriptionFilterClusterSeed seed)
{
    internal SubscriptionFilterClusterSeed Seed { get; } = seed;
    internal ReceiveSubscriptionRequest? OriginalRequest
    {
        get; set;
    }
    internal ReceiveSubscriptionResult? OriginalClaim
    {
        get; set;
    }
    internal List<(SubscriptionDeliveryCommand Request, CommitReceipt Receipt, long Epoch)> Receipts { get; } = [];
    internal List<(ReceiveSubscriptionRequest Request, ReceiveSubscriptionResult Result, long Epoch)> Claims { get; } = [];
    internal List<(SubscriptionDeliveryCommand Request, ManagedCode.Communication.Result<CommitReceipt> Failure, long Epoch, ErrorCode Code)> Refusals { get; } = [];
    internal long PolicyEpoch { get; set; } = 1;
    internal long Generation { get; set; } = 1;
    internal bool Paused
    {
        get; set;
    }
    internal SubscriptionDefinition IndependentDefinition
    {
        get; set;
    } = new(seed.Manager.Principal.Id)
    {
        Policy = new()
        {
            MaxWindow = SubscriptionFilterClusterProtocol.Window
        }
    };
    internal List<(SetSubscriptionPausedRequest Request, SubscriptionInfo Result)> PauseHistory { get; } = [];
    internal List<(ConfigureSubscriptionRequest Request, SubscriptionInfo Result)> ConfigureHistory { get; } = [];
    internal long Checkpoint
    {
        get; set;
    }
    internal long Issued
    {
        get; set;
    }
    internal long PrimaryGeneration { get; set; } = 1;
    internal long PrimaryCheckpoint { get; set; } = 1;
    internal long PrimaryIssued { get; set; } = 3;
    internal bool PrimaryPaused { get; set; } = true;
    internal EventSourcePage? Source
    {
        get; set;
    }
    internal OutboxStatus? Outbox
    {
        get; set;
    }
    internal CommandRequest? HealthyPublish
    {
        get; set;
    }
    internal CommitReceipt? HealthyReceipt
    {
        get; set;
    }
    internal long Tail { get; set; } = 4;
}
