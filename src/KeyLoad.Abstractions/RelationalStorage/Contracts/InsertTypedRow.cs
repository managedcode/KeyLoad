namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.InsertTypedRow)]
internal sealed record InsertTypedRow(
    [property: Orleans.Id(0)] string Collection,
    [property: Orleans.Id(1)] string Json) : Mutation(Collection);
