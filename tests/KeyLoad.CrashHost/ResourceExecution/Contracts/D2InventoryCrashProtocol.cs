namespace KeyLoad.CrashHost.Features.ResourceExecution;

internal enum D2InventoryCrashCut
{
    EntryAcknowledged,
    CommitAcknowledged,
    AppliedAcknowledged
}

internal static class D2InventoryCrashProtocol
{
    private const string SelectedIdText = "46171b42-477b-45c7-b83a-195d052242a8";
    private const string SourceBootstrapIdText = "98369464-510b-42d9-b58d-bdc4555a7962";
    private const string TargetBootstrapIdText = "5fdf23c9-b71b-4fc9-9367-197427230f9b";
    private const string RegistrationIdText = "4de148e1-2156-42cd-b29b-dac6540f1247";
    private const string AIdText = "4d5b9714-4feb-4675-b6b3-10e19b5fa9ea";
    private const string BIdText = "beb28d6b-6bfe-4ba9-bf50-9cbbef080802";
    private const string CIdText = "9a41d0fb-caf6-4f56-8e2a-9ff75904c5a2";
    private const string P0IdText = "ecbd9964-d8b4-46ec-8e87-198296167f2b";
    private const string P1IdText = "8f325d46-ed9a-4fd8-aea8-debff763edec";
    internal const string Mode = "d2-inventory-recovery";
    internal const string SourceDirectory = "source-owner";
    internal const string TargetDirectory = "target-owner";
    internal const string EvidenceFile = "d2-original.native";
    internal const string ReceiptFile = "d2-receipt.native";
    internal const string EvidenceAlias = "keyload.tests.d2-inventory-crash-evidence.v1";
    internal const string Principal = "root";
    internal const string Tenant = "d2-tenant";
    internal const string Database = "d2-database";
    internal const string Domain = "d2-domain";
    internal const string OtherDomain = "d2-other-domain";
    internal const string ResourceA = "inventory-a";
    internal const string ResourceB = "inventory-b";
    internal const string ResourceC = "inventory-unused";
    internal const string DocumentId = "row";
    internal const string Json = "{\"value\":2}";
    internal const string MutationKind = "putDocument";
    internal const int Version = 1;
    internal const int FirstPageSize = 1;
    internal const int FirstField = 0;
    internal const int SecondField = 1;
    internal const int ThirdField = 2;
    internal const int FourthField = 3;
    internal const int FifthField = 4;
    internal const int SixthField = 5;
    internal const long InitialRevision = 1;
    internal const long EmptyRevision = 0;
    internal const long IndexStep = 1;
    internal const string P0 = "p0";
    internal const string P1 = "p1";
    internal const string P2 = "p2";
    internal static readonly Guid SelectedId = Guid.Parse(SelectedIdText);
    internal static readonly Guid SourceBootstrapId = Guid.Parse(SourceBootstrapIdText);
    internal static readonly Guid TargetBootstrapId = Guid.Parse(TargetBootstrapIdText);
    internal static readonly Guid RegistrationId = Guid.Parse(RegistrationIdText);
    internal static readonly Guid AId = Guid.Parse(AIdText);
    internal static readonly Guid BId = Guid.Parse(BIdText);
    internal static readonly Guid CId = Guid.Parse(CIdText);
    internal static readonly Guid P0Id = Guid.Parse(P0IdText);
    internal static readonly Guid P1Id = Guid.Parse(P1IdText);
    internal static PartitionRef Partition(string key) => new(Tenant, Database, Domain, key);
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(D2InventoryCrashProtocol.EvidenceAlias)]
internal sealed record D2InventoryCrashEvidence(
    [property: global::Orleans.Id(D2InventoryCrashProtocol.FirstField)] ReplicatedOperation Original,
    [property: global::Orleans.Id(D2InventoryCrashProtocol.SecondField)] ReplicatedOperation PrefixOriginal,
    [property: global::Orleans.Id(D2InventoryCrashProtocol.ThirdField)] CommitReceipt PrefixReceipt,
    [property: global::Orleans.Id(D2InventoryCrashProtocol.FourthField)] long SelectedIndex,
    [property: global::Orleans.Id(D2InventoryCrashProtocol.FifthField)] long PrefixPosition,
    [property: global::Orleans.Id(D2InventoryCrashProtocol.SixthField)] ReplicatedOperation BeforeSelectedOperation);
