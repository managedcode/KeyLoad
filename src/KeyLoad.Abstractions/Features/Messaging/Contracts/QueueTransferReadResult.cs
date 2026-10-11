namespace KeyLoad;

/// <summary>Contains the authorized source transfer state, or successful absence.</summary>
/// <param name="Transfer">The projected source intent; null means no matching intent exists.</param>
[Orleans.GenerateSerializer, Orleans.Alias(PublicReadResultAliases.Transfer)]
public sealed record QueueTransferReadResult([property: Orleans.Id(0)] QueueTransferInspection? Transfer);
