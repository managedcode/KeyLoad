namespace KeyLoad.UnitTests.Features.OrleansExecution;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class RequestCqrsHostedDataSourceAttribute(bool textMaintenance = false,
    bool annMaintenance = false) : DataSourceGeneratorAttribute<RequestCqrsHostedRuntimeFixture>
{
    public bool RuntimeTextMaintenance { get; } = textMaintenance;
    public bool RuntimeAnnMaintenance { get; } = annMaintenance;

    protected override IEnumerable<Func<RequestCqrsHostedRuntimeFixture>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        if (RuntimeTextMaintenance || RuntimeAnnMaintenance)
        {
            yield return () => new RequestCqrsHostedRuntimeFixture(textMaintenance: RuntimeTextMaintenance,
                annMaintenance: RuntimeAnnMaintenance);
            yield break;
        }
        yield return () => SharedDataSources.GetOrCreate<RequestCqrsHostedRuntimeFixture>(
            SharedType.PerTestSession, metadata, null, static () => new RequestCqrsHostedRuntimeFixture());
    }
}
