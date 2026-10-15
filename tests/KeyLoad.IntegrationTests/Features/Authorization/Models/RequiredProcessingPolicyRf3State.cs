namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed class RequiredProcessingPolicyRf3State(PartitionRef partition, string worker, string key, bool header)
{
    internal const string Queue = "required-input-queue";
    internal const string Derived = "required-input-derived";
    internal const string Before = "required.before.read";
    internal const string After = "required.after.read";
    internal const string Denied = "Required worker input is protected by a missing raw-read grant.";
    internal const string EpochChanged = "The principal policy changed since this command was evaluated.";
    internal const string Tool = "keyload_messages_process";
    internal PartitionRef Partition { get; } = partition;
    internal string Worker { get; } = worker;
    internal string Key { get; } = key;
    internal bool Header { get; } = header;
    internal ResourceDefinition Resource { get; set; } = null!;
    internal List<(CommandRequest Request, CommitReceipt Receipt)> Producers { get; } = [];
    internal List<Delivery> Deliveries { get; } = [];
    internal ProcessingRequest Refused { get; set; } = null!;
    internal ProcessingRequest Original { get; set; } = null!;
    internal CommitReceipt OriginalReceipt { get; set; } = null!;
    internal ProcessingRequest Healthy { get; set; } = null!;
    internal CommitReceipt HealthyReceipt { get; set; } = null!;
    internal QueueLaneRef Lane => new(Partition, Queue);
    internal EntityRef Reference(string id) => new(Partition, Derived, id);
    internal string Payload => Header ? "{\"value\":1}" : "{\"secret\":\"BODY-PROCESSING-CANARY\",\"value\":1}";
    internal string Headers => Header ? "{\"secret\":\"HEADER-PROCESSING-CANARY\",\"trace\":\"one\"}" : "{\"trace\":\"one\"}";
    internal static string EffectJson(string id) => id == "first" ? "{\"processed\":1}" : "{\"processed\":2}";
    internal ProcessingRequest Processing(Delivery delivery, string id) => new(Guid.NewGuid(), Lane, delivery.Token,
        "required-handler", 1L, [new PutDocument(Derived, id, EffectJson(id), 0L)]);
    internal PrincipalRecord Principal(long epoch, bool revoked) => new(Worker, Partition.TenantId,
        [new(Partition.DatabaseId, Queue, Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect | Capability.Query),
         new(Partition.DatabaseId, Derived, Capability.DocumentsWrite | Capability.DocumentsRead)], [Before])
    {
        PolicyEpoch = epoch,
        Revoked = revoked
    };
}
