using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests.AnalyticsStore;

[Orleans.GenerateSerializer]
[Orleans.Alias(UserAnalyticsProcessProtocol.EvidenceAlias)]
internal sealed record UserAnalyticsProcessOriginal(
    [property: Orleans.Id(0)] StoreIdentity Identity,
    [property: Orleans.Id(1)] long Position,
    [property: Orleans.Id(2)] string[] Rows,
    [property: Orleans.Id(3)] Command<DatabaseCommand> Operation,
    [property: Orleans.Id(4)] CommitReceipt Receipt,
    [property: Orleans.Id(5)] byte[] SigningKeyDigest);
