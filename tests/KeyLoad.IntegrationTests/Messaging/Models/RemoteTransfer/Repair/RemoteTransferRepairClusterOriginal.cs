using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record RemoteTransferRepairClusterOriginal(RemoteTransferColdSeed Seed,
    QueueTransferInspection Intent, RemoteTransferCoordinationHint Hint, CommandRequest Failed,
    QueueTransferRepairStage Stage, QueueTransferReceiptInspection? AcceptedProof);
