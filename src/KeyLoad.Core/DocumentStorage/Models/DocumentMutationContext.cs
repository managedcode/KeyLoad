namespace KeyLoad.Core.DocumentStorage;

/// <summary>Carries one document's key and decoded prior image only within its atomic transaction.</summary>
/// <param name="Key">The canonical document key owned by the current mutation.</param>
/// <param name="Before">The decoded image observed before handler validation.</param>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.DocumentMutationContext)]
internal readonly record struct DocumentMutationContext(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.DocumentMutationContextFields.Key)] byte[] Key,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.DocumentMutationContextFields.Before)] DocumentRecord? Before);
