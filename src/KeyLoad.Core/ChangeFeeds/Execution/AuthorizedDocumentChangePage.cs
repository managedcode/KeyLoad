using System.Collections.Immutable;

namespace KeyLoad.Core;

/// <summary>Immutable authorized changes and continuation state from one read cut.</summary>
/// <typeparam name="T">Owned projected change type.</typeparam>
/// <param name="Changes">Ordered owned projections.</param>
/// <param name="Cursor">Signed continuation cursor.</param>
/// <param name="ThroughSequence">Last examined outbox sequence.</param>
/// <param name="Tail">Tail of the captured outbox.</param>
/// <param name="FirstAvailable">First retained outbox sequence.</param>
/// <param name="HasMore">Whether later source entries remain.</param>
/// <param name="CutPosition">Storage position represented by this page.</param>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.AuthorizedDocumentChangePage)]
public sealed record AuthorizedDocumentChangePage<T>(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.AuthorizedDocumentChangePageFields.Changes)] ImmutableArray<T> Changes,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.AuthorizedDocumentChangePageFields.Cursor)] string Cursor,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.AuthorizedDocumentChangePageFields.ThroughSequence)] long ThroughSequence,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.AuthorizedDocumentChangePageFields.Tail)] long Tail,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.AuthorizedDocumentChangePageFields.FirstAvailable)] long FirstAvailable,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.AuthorizedDocumentChangePageFields.HasMore)] bool HasMore,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.AuthorizedDocumentChangePageFields.CutPosition)] long CutPosition);
