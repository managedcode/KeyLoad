namespace KeyLoad;

/// <summary>Represents a persisted document revision and its access metadata.</summary>
/// <param name="Reference">Identifies the document.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
/// <param name="Json">Contains the serialized operation result.</param>
/// <param name="Access">Provides optional row access metadata.</param>
/// <param name="UpdatedAt">Records the update time.</param>
/// <param name="Deleted">Marks a tombstone when true.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.DocumentRecord)]
public sealed record DocumentRecord([property: Orleans.Id(0)] EntityRef Reference, [property: Orleans.Id(1)] long Revision, [property: Orleans.Id(2)] string Json, [property: Orleans.Id(3)] RowAccess Access,
    [property: Orleans.Id(4)] DateTimeOffset UpdatedAt, [property: Orleans.Id(5)] bool Deleted = false);
