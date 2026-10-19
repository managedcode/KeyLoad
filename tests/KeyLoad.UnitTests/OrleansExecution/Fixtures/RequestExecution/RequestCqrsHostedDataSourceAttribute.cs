namespace KeyLoad.UnitTests.Features.OrleansExecution;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class RequestCqrsHostedDataSourceAttribute(bool textMaintenance = false,
    bool annMaintenance = false) : DataSourceGeneratorAttribute<RequestCqrsHostedRuntimeFixture>
{
    public bool TextMaintenance { get; } = textMaintenance;
    public bool AnnMaintenance { get; } = annMaintenance;

    protected override IEnumerable<Func<RequestCqrsHostedRuntimeFixture>> GenerateDataSources(DataGeneratorMetadata metadata)
    {
        if (TextMaintenance || AnnMaintenance)
        {
            yield return () => new RequestCqrsHostedRuntimeFixture(textMaintenance: TextMaintenance,
                annMaintenance: AnnMaintenance);
            yield break;
        }
        yield return () => SharedDataSources.GetOrCreate<RequestCqrsHostedRuntimeFixture>(
            SharedType.PerTestSession, metadata, null, static () => new RequestCqrsHostedRuntimeFixture());
    }
}
