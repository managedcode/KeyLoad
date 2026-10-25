using KeyLoad.Core.Messaging;

namespace KeyLoad.ClusterTests.Messaging;

internal sealed record RemoteTransferRepairClusterOriginal(RemoteTransferColdSeed Seed,
    QueueTransferInspection Intent, RemoteTransferCoordinationHint Hint, CommandRequest Failed,
    QueueTransferRepairStage Stage, QueueTransferReceiptInspection? AcceptedProof);
