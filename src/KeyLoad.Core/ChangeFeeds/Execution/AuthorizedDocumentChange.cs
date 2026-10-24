namespace KeyLoad.Core;

/// <summary>One change whose historical and current row visibility passed authorization.</summary>
/// <param name="Sequence">Outbox sequence.</param>
/// <param name="Commit">Commit token of the source mutation.</param>
/// <param name="CommittedAt">Original commit time.</param>
/// <param name="Before">Visible previous document, when available.</param>
/// <param name="After">Visible document after the mutation.</param>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.AuthorizedDocumentChange)]
public sealed record AuthorizedDocumentChange(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.AuthorizedDocumentChangeFields.Sequence)] long Sequence,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.AuthorizedDocumentChangeFields.Commit)] CommitToken Commit,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.AuthorizedDocumentChangeFields.CommittedAt)] DateTimeOffset CommittedAt,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.AuthorizedDocumentChangeFields.Before)] DocumentRecord? Before,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.AuthorizedDocumentChangeFields.After)] DocumentRecord After);
