using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Returns a document revision with redaction metadata for the caller.</summary>
/// <param name="Reference">Identifies the document.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
/// <param name="Json">Contains the serialized operation result.</param>
/// <param name="Redacted">Indicates whether the document was redacted.</param>
/// <param name="RedactedFields">Lists fields removed from the result.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.DocumentResult)]
public sealed record DocumentResult([property: Orleans.Id(0)] EntityRef Reference, [property: Orleans.Id(1)] long Revision, [property: Orleans.Id(2)] string Json, [property: Orleans.Id(3)] bool Redacted, [property: Orleans.Id(4)] ImmutableArray<string> RedactedFields);
