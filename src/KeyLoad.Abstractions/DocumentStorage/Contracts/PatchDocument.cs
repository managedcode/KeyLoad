using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Applies field patches to a document at an expected revision.</summary>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Patches">Lists field changes to apply.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.PatchDocument)]
public sealed record PatchDocument([property: Orleans.Id(0)] string Collection, [property: Orleans.Id(1)] string Id, [property: Orleans.Id(2)] ImmutableArray<FieldPatch> Patches, [property: Orleans.Id(3)] long ExpectedRevision) : Mutation(Collection);
