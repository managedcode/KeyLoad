using KeyLoad.Core.Messaging;

namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record RemoteTransferRepairClusterOriginal(RemoteTransferColdSeed Seed,
    QueueTransferInspection Intent, RemoteTransferCoordinationHint Hint, CommandRequest Failed,
    QueueTransferRepairStage Stage, QueueTransferReceiptInspection? AcceptedProof);
