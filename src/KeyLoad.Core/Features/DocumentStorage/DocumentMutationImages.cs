namespace KeyLoad.Core.Features.DocumentStorage;

/// <summary>Carries one document's key and decoded prior image only within its atomic transaction.</summary>
/// <param name="Key">The canonical document key owned by the current mutation.</param>
/// <param name="Before">The decoded image observed before handler validation.</param>
internal readonly record struct DocumentMutationContext(byte[] Key, DocumentRecord? Before);

/// <summary>Carries the receipt and exact final image staged by one document mutation.</summary>
/// <param name="Receipt">The mutation's durable receipt.</param>
/// <param name="After">The constructed final image, including a deletion tombstone.</param>
internal readonly record struct DocumentMutationResult(MutationReceipt Receipt, DocumentRecord After);
