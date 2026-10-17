namespace KeyLoad;

/// <summary>Queue lifecycle metadata without payloads, headers, lease owners or delivery tokens.</summary>
/// <param name="Id">Message identifier.</param>
/// <param name="State">Persisted lifecycle; this observation does not sweep expiry.</param>
/// <param name="Attempts">Persisted delivery attempt count.</param>
/// <param name="StateVersion">Persisted lifecycle revision.</param>
/// <param name="NotBefore">Optional persisted schedule time.</param>
/// <param name="ExpiresAt">Optional persisted expiry time.</param>
/// <param name="LeaseUntil">Optional persisted lease expiry, without ownership authority.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminQueueItem)]
public sealed record AdminQueueItem([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] MessageState State, [property: Orleans.Id(2)] int Attempts, [property: Orleans.Id(3)] long StateVersion,
    [property: Orleans.Id(4)] DateTimeOffset? NotBefore, [property: Orleans.Id(5)] DateTimeOffset? ExpiresAt, [property: Orleans.Id(6)] DateTimeOffset? LeaseUntil);
