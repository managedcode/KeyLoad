namespace KeyLoad;

/// <summary>Identifies the document to retrieve.</summary>
/// <param name="Reference">The document's partition and entity identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.GetDocumentRequest)]
public sealed record GetDocumentRequest([property: Orleans.Id(0)] EntityRef Reference);
