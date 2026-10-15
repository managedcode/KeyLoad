namespace KeyLoad.CrashHost.Features.Messaging;

[Orleans.GenerateSerializer, Orleans.Alias(ExternalQueueWorkerCrashProtocol.ClaimAlias)]
internal sealed record ExternalQueueWorkerCrashClaim([property: Orleans.Id(0)] Delivery Delivery,
    [property: Orleans.Id(1)] Guid AcknowledgementId, [property: Orleans.Id(2)] Guid FirstRenewalId);
