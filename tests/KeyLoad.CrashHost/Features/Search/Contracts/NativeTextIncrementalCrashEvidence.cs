using KeyLoad.Orleans;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.CrashHost.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeTextIncrementalCrashProtocol.OriginalAlias)]
internal sealed record NativeTextIncrementalCrashOriginal(
    [property: global::Orleans.Id(0)] TextIndexMaintenanceRequest Request,
    [property: global::Orleans.Id(1)] CommandRequest Mutation,
    [property: global::Orleans.Id(2)] CommitReceipt Receipt);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeTextIncrementalCrashProtocol.ResultAlias)]
internal sealed record NativeTextIncrementalCrashResult(
    [property: global::Orleans.Id(0)] TextMaintenanceCapabilityResult Complete,
    [property: global::Orleans.Id(1)] CommitReceipt OriginalReceipt,
    [property: global::Orleans.Id(2)] ProjectionBatchResult? Checkpoint,
    [property: global::Orleans.Id(3)] DocumentResult Ukrainian,
    [property: global::Orleans.Id(4)] DocumentResult? English,
    [property: global::Orleans.Id(5)] long AppliedPosition,
    [property: global::Orleans.Id(6)] NativeTextIncrementalRecord[] Records,
    [property: global::Orleans.Id(7)] NativeTextIncrementalPosting[] Postings,
    [property: global::Orleans.Id(8)] NativeTextCanonicalState Canonical,
    [property: global::Orleans.Id(9)] long StorePosition,
    [property: global::Orleans.Id(10)] RankedDocument[] SelectedPage,
    [property: global::Orleans.Id(11)] RankedDocument[] RemovedUkrainian,
    [property: global::Orleans.Id(12)] RankedDocument[] RemovedEnglish);
