namespace KeyLoad.CrashHost.Features.ResourceExecution;

[Orleans.GenerateSerializer, Orleans.Alias(D2ImportCrashProtocol.InputAlias)]
internal sealed record D2ImportCrashInput(
    [property: Orleans.Id(D2ImportCrashProtocol.FirstField)] PhysicalShardRecord Owner,
    [property: Orleans.Id(D2ImportCrashProtocol.SecondField)] ReplicatedOperation Original,
    [property: Orleans.Id(D2ImportCrashProtocol.ThirdField)] long BeforePosition,
    [property: Orleans.Id(D2ImportCrashProtocol.FourthField)] long BeforeIndex);
