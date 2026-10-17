namespace KeyLoad;

/// <summary>Creates or replaces a document in a collection.</summary>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Json">Contains the serialized operation result.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
/// <param name="Access">Provides optional row access metadata.</param>
/// <param name="ExplicitReplacement">Allows an explicit replacement when true.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.PutDocument)]
public sealed record PutDocument([property: Orleans.Id(0)] string Collection, [property: Orleans.Id(1)] string Id, [property: Orleans.Id(2)] string Json, [property: Orleans.Id(3)] long? ExpectedRevision = null,
    [property: Orleans.Id(4)] RowAccess? Access = null, [property: Orleans.Id(5)] bool ExplicitReplacement = false) : Mutation(Collection);
