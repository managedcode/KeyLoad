namespace KeyLoad.CrashHost.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeTextOnlineCrashScenario.OriginalAlias)]
internal sealed record NativeTextOnlineCrashOriginal(
    [property: global::Orleans.Id(0)] OnlineTextIndexMaintenanceRequest Request,
    [property: global::Orleans.Id(1)] CommitReceipt MutationReceipt,
    [property: global::Orleans.Id(2)] CommandRequest Mutation);
