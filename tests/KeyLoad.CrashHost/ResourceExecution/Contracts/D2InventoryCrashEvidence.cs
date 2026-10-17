namespace KeyLoad.CrashHost.Features.ResourceExecution;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(D2InventoryCrashProtocol.EvidenceAlias)]
internal sealed record D2InventoryCrashEvidence(
    [property: global::Orleans.Id(D2InventoryCrashProtocol.FirstField)] ReplicatedOperation Original,
    [property: global::Orleans.Id(D2InventoryCrashProtocol.SecondField)] ReplicatedOperation PrefixOriginal,
    [property: global::Orleans.Id(D2InventoryCrashProtocol.ThirdField)] CommitReceipt PrefixReceipt,
    [property: global::Orleans.Id(D2InventoryCrashProtocol.FourthField)] long SelectedIndex,
    [property: global::Orleans.Id(D2InventoryCrashProtocol.FifthField)] long PrefixPosition,
    [property: global::Orleans.Id(D2InventoryCrashProtocol.SixthField)] ReplicatedOperation BeforeSelectedOperation);
