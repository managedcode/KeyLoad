namespace KeyLoad.RecoveryTests.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(TextIncrementalCrashProtocol.OriginalAlias)]
internal sealed record TextIncrementalCrashOriginal(
    [property: global::Orleans.Id(0)] TextIndexMaintenanceRequest Request,
    [property: global::Orleans.Id(1)] CommandRequest Mutation,
    [property: global::Orleans.Id(2)] CommitReceipt Receipt);
