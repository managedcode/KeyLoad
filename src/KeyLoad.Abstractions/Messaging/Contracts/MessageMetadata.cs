namespace KeyLoad;

/// <summary>Stores queue delivery state, sequencing, lease, and failure metadata.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="State">Describes the expected stream state.</param>
/// <param name="Attempts">Specifies the attempts value.</param>
/// <param name="StateVersion">Specifies the state version value.</param>
/// <param name="ReadySequence">Specifies the ready sequence value.</param>
/// <param name="NotBefore">Sets the optional earliest delivery time.</param>
/// <param name="ExpiresAt">Sets the optional expiration time.</param>
/// <param name="LeaseOwner">Specifies the lease owner value.</param>
/// <param name="LeaseVersion">Identifies the lease version.</param>
/// <param name="LeaseUntil">Specifies the lease until value.</param>
/// <param name="DeliveryGeneration">Identifies the delivery generation.</param>
/// <param name="SafeFailureCode">Specifies the safe failure code value.</param>
/// <param name="ParkedSequence">The persisted dead-letter ordering sequence, or zero when not parked.</param>
/// <param name="EnqueueSequence">The immutable strict-order sequence at original enqueue, or zero for competing consumers.</param>
/// <param name="ActiveOrderSequence">The current strict-order position, or zero when the message has no active order entry.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.MessageMetadata)]
public sealed record MessageMetadata([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] MessageState State, [property: Orleans.Id(2)] int Attempts, [property: Orleans.Id(3)] long StateVersion, [property: Orleans.Id(4)] long ReadySequence,
    [property: Orleans.Id(5)] DateTimeOffset? NotBefore, [property: Orleans.Id(6)] DateTimeOffset? ExpiresAt, [property: Orleans.Id(7)] string? LeaseOwner = null, [property: Orleans.Id(8)] long LeaseVersion = MessageMetadata.DefaultLeaseVersion,
    [property: Orleans.Id(9)] DateTimeOffset? LeaseUntil = null, [property: Orleans.Id(10)] long DeliveryGeneration = MessageMetadata.DefaultDeliveryGeneration, [property: Orleans.Id(11)] string? SafeFailureCode = null, [property: Orleans.Id(12)] long ParkedSequence = MessageMetadata.DefaultLeaseVersion,
    [property: Orleans.Id(13)] long EnqueueSequence = MessageMetadata.DefaultLeaseVersion,
    [property: Orleans.Id(14)] long ActiveOrderSequence = MessageMetadata.DefaultLeaseVersion)
{
    private const int DefaultLeaseVersion = 0;
    private const int DefaultDeliveryGeneration = 1;
}
