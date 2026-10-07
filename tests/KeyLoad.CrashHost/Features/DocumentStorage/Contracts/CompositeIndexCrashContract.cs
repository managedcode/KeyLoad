
namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal static class CompositeIndexCrashContract
{
    internal const string PrepareMode = "composite-index-prepare";
    internal const string FaultMode = "composite-index-inflight";
    internal const string RecoverMode = "composite-index-recover";
    internal const string VerifyMode = "composite-index-verify";
    internal const string Collection = "process-index-documents";
    internal const string IndexName = "by-label-rank";
    internal const string RankIndex = "by-rank";
    internal const string RankPath = "/rank";
    internal const string SeededFile = "composite-index-seeded.json";
    internal const string LabelPath = "/label";
    internal const string TombstoneJson = "{}";
    internal const string IndexAccessPathPrefix = "index:";
    internal const string QueryPrefix = "SELECT * FROM \"" + Collection + "\" WHERE label = '";
    internal const string QuerySuffix = "'";
    internal const string UnsupportedModeMessage = "The composite-index CrashHost mode is unsupported.";
    internal const string PreparedFile = "composite-index-prepared.json";
    internal const string BeforeCutFile = "composite-index-before-cut.json";
    internal const string RecoveredFile = "composite-index-recovered.json";
    internal const string HealthyFile = "composite-index-healthy.json";
    internal const string FinalFile = "composite-index-final.json";
    internal const string FirstId = "index-a";
    internal const string SecondId = "index-b";
    internal const string DeletedId = "index-c";
    internal const string ConflictId = "index-d";
    internal const string OtherPartitionId = "index-z";
    internal const string FirstInitialJson = "{\"label\":\"alpha\",\"rank\":1}";
    internal const string SecondInitialJson = "{\"label\":\"beta\",\"rank\":2}";
    internal const string DeletedInitialJson = "{\"label\":\"gamma\",\"rank\":3}";
    internal const string ReplacedJson = "{\"label\":\"delta\",\"rank\":4}";
    internal const string PatchedJson = "{\"label\":\"epsilon\",\"rank\":5}";
    internal const string ConflictJson = "{\"label\":\"rollback\",\"rank\":6}";
    internal const string FaultInsertJson = "{\"label\":\"zeta\",\"rank\":7}";
    internal const string FaultPatchJson = "{\"label\":\"theta\",\"rank\":8}";
    internal const string HealthyJson = "{\"label\":\"healthy\",\"rank\":9}";
    internal const string PartitionKey = "process-index-partition";
    internal const string OtherPartitionKey = "process-index-other-partition";
    internal const string ValueAlpha = "alpha";
    internal const string ValueBeta = "beta";
    internal const string ValueGamma = "gamma";
    internal const string ValueDelta = "delta";
    internal const string ValueEpsilon = "epsilon";
    internal const string ValueRollback = "rollback";
    internal const string ValueZeta = "zeta";
    internal const string ValueTheta = "theta";
    internal const string ValueHealthy = "healthy";
    internal const string CollectionCommandText = "a1473571-8ba6-4191-8ce7-e3f752a9bc01";
    internal const string SeedCommandText = "b2584682-9cb7-42a2-9df8-f4a863bacd12";
    internal const string ReplaceCommandText = "c3695793-adc8-43b3-ae09-05b974cbde23";
    internal const string PatchCommandText = "d47a68a4-bed9-44c4-bf1a-16c085dcef34";
    internal const string DeleteCommandText = "e58b79b5-cfea-45d5-c02b-27d196edf045";
    internal const string ConflictCommandText = "f69c8ac6-d0fb-46e6-d13c-38e2a7fe0156";
    internal const string OtherPartitionCommandText = "07ad9bd7-e10c-47f7-e24d-49f3b80f1267";
    internal const string FaultCommandText = "18beace8-f21d-4808-f35e-5a04c9102378";
    internal const string HealthyCommandText = "29cfbdf9-032e-4919-a46f-6b15da214389";
    internal static PartitionRef Partition { get; } = new(CrashFixtureValues.Tenant,
        CrashFixtureValues.Database, CrashFixtureValues.Orders, PartitionKey);
    internal static PartitionRef OtherPartition { get; } = Partition with { PartitionKey = OtherPartitionKey };
}
