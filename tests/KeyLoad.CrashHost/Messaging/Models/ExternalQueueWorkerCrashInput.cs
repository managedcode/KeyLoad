namespace KeyLoad.CrashHost.Features.Messaging;

[Orleans.GenerateSerializer, Orleans.Alias(ExternalQueueWorkerCrashProtocol.InputAlias)]
internal sealed record ExternalQueueWorkerCrashInput([property: Orleans.Id(0)] string Endpoint,
    [property: Orleans.Id(1)] string Credential, [property: Orleans.Id(2)] QueueLaneRef Lane,
    [property: Orleans.Id(3)] Guid ReceiveId, [property: Orleans.Id(4)] string PrincipalId)
{
    private const string RedactedDisplay = "ExternalQueueWorkerCrashInput(<private>)";

    public override string ToString() => RedactedDisplay;
}
