namespace KeyLoad.Core.Features.DocumentStorage;

/// <summary>Carries one document's key and decoded prior image only within its atomic transaction.</summary>
/// <param name="Key">The canonical document key owned by the current mutation.</param>
/// <param name="Before">The decoded image observed before handler validation.</param>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.DocumentMutationContext)]
internal readonly record struct DocumentMutationContext(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.DocumentMutationContextFields.Key)] byte[] Key,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.DocumentMutationContextFields.Before)] DocumentRecord? Before);

/// <summary>Carries the receipt and exact final image staged by one document mutation.</summary>
/// <param name="Receipt">The mutation's durable receipt.</param>
/// <param name="After">The constructed final image, including a deletion tombstone.</param>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.DocumentMutationResult)]
internal readonly record struct DocumentMutationResult(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.DocumentMutationResultFields.Receipt)] MutationReceipt Receipt,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.DocumentMutationResultFields.After)] DocumentRecord After);
