namespace KeyLoad.UnitTests.Features.ClusterRouting;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class RuntimeJournalNativeDataSourceAttribute : DataSourceGeneratorAttribute<RuntimeJournalNativeFixture>
{
    protected override IEnumerable<Func<RuntimeJournalNativeFixture>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        yield return () => SharedDataSources.GetOrCreate<RuntimeJournalNativeFixture>(
            SharedType.PerTestSession, metadata, null, static () => new RuntimeJournalNativeFixture());
    }
}
