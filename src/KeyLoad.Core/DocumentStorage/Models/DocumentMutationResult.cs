namespace KeyLoad.Core.DocumentStorage;

/// <summary>Carries the receipt and exact final image staged by one document mutation.</summary>
/// <param name="Receipt">The mutation's durable receipt.</param>
/// <param name="After">The constructed final image, including a deletion tombstone.</param>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.DocumentMutationResult)]
internal readonly record struct DocumentMutationResult(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.DocumentMutationResultFields.Receipt)] MutationReceipt Receipt,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.DocumentMutationResultFields.After)] DocumentRecord After);
