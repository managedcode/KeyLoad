namespace KeyLoad.Query.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(GlobalBranchProtocol.KindAlias)]
internal enum GlobalBranchKind
{
    Text = 0,
    Vector = 1,
    Graph = 2
}
