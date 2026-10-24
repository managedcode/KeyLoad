namespace KeyLoad.RecoveryTests.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(TextOnlineCrashScenario.OriginalAlias)]
internal sealed record TextOnlineCrashOriginal(
    [property: global::Orleans.Id(0)] OnlineTextIndexMaintenanceRequest Request,
    [property: global::Orleans.Id(1)] CommitReceipt MutationReceipt,
    [property: global::Orleans.Id(2)] CommandRequest Mutation);
