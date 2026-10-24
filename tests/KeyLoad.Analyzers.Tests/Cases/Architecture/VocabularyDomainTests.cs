namespace KeyLoad.Analyzers.Tests.CodeQuality;

internal sealed class VocabularyDomainTests
{
    [Test]
    [Arguments(typeof(KeyLoad.SampleData))]
    [Arguments(typeof(KeyLoad.SampleRecord))]
    [Arguments(typeof(KeyLoad.ReadSamplesRequest))]
    [Arguments(typeof(KeyLoad.LatestSampleResult))]
    public async Task ActualDomainTypesKeepTheirMetadataIdentityAcrossMemberShapes(Type type)
    {
        var word = RuntimePolicyAnalyzerFixture.Word("DataWord");
        var source = $$"""
            internal class Subject
            {
                internal {{type.FullName}}[] Values;
                internal System.Collections.Generic.List<{{type.FullName}}> Items { get; set; }
                internal {{type.FullName}} Read() => default;
                internal void Observe({{type.FullName}} value)
                {
                    var descriptions = new[] { "{{word}}", "{{word}}s" };
                    _ = descriptions;
                }
                internal object {{type.Name}} => null;
            }
            """;
        var findings = await RuntimePolicyAnalyzerFixture.AnalyzeAsync(new UnauthorizedImplementationTermAnalyzer(), source);
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task UnrelatedMemberNamesAndRepeatedTextNeverAcquireDomainOwnership()
    {
        var word = RuntimePolicyAnalyzerFixture.Word("DataWord");
        var source = $$"""
            internal class Subject
            {
                internal int {{word}};
                internal object Read() => new[] { "{{word}}", "{{word}}s" };
            }
            """;
        var findings = await RuntimePolicyAnalyzerFixture.AnalyzeAsync(new UnauthorizedImplementationTermAnalyzer(), source);
        await Assert.That(findings.Length).IsEqualTo(3);
    }

    [Test]
    [Arguments("/repo/src/KeyLoad/TimeSeries/Subject.cs")]
    [Arguments("/repo/src/KeyLoad/TimeSeries/Subject.g.cs")]
    public async Task TimeSeriesSourceIdentityKeepsTheApprovedDomainMeaning(string path)
    {
        var word = RuntimePolicyAnalyzerFixture.Word("DataWord");
        var source = $"internal class Subject {{ internal int {word}; }}";
        var findings = await RuntimePolicyAnalyzerFixture.AnalyzeAsync(new UnauthorizedImplementationTermAnalyzer(), source, path);
        await Assert.That(findings).IsEmpty();
    }
    [Test]
    public async Task ActualOperationalMeasurementRecordsKeepTheirDataVocabulary()
    {
        var word = RuntimePolicyAnalyzerFixture.Word("DataWord");
        var type = typeof(KeyLoad.AdminNodeSnapshot).FullName;
        var source = $$"""
            internal class Subject
            {
                internal {{type}} Read() { const string description = "{{word}}"; return default; }
            }
            """;
        var findings = await RuntimePolicyAnalyzerFixture.AnalyzeAsync(new UnauthorizedImplementationTermAnalyzer(), source);
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ActualOrleansCodecInterfacesRetainOnlyTheirResolvedDomainArguments(bool domain)
    {
        var word = RuntimePolicyAnalyzerFixture.Word("DataWord");
        var argument = domain ? typeof(KeyLoad.SampleRecord).FullName : "Contract";
        var source = $$"""
            internal interface Contract : Orleans.Serialization.Codecs.IFieldCodec<{{argument}}> { }
            internal class Subject
            {
                internal Contract Read() { const string description = "{{word}}"; return default; }
            }
            """;
        var findings = await RuntimePolicyAnalyzerFixture.AnalyzeAsync(new UnauthorizedImplementationTermAnalyzer(), source);
        await Assert.That(findings.Length).IsEqualTo(domain ? 0 : 1);
    }

}
