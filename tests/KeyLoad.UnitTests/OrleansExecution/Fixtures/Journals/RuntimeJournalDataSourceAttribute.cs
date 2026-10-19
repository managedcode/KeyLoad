namespace KeyLoad.UnitTests.Features.OrleansExecution;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class RuntimeJournalDataSourceAttribute : DataSourceGeneratorAttribute<RuntimeRuntimeJournalFixture>
{
    protected override IEnumerable<Func<RuntimeRuntimeJournalFixture>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        yield return () => SharedDataSources.GetOrCreate<RuntimeRuntimeJournalFixture>(
            SharedType.PerTestSession, metadata, null, static () => new RuntimeRuntimeJournalFixture());
    }
}
