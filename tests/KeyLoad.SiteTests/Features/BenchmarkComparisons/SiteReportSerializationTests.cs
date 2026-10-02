using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteReportSerializationTests
{
    [Test]
    public async Task AC_CQ_013_GeneratedReportContextPreservesOptionsAcrossRealNodeRequests()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        var request = new ProbeRequest(SiteTokens.MedianOperation, Values: SiteTokens.OddMedianValues);
        var before = await SiteNodeProbe.RunAsync(inputs, request, token);
        await Assert.That(before.Value.GetDouble()).IsEqualTo(5d);

        var sharedOptions = SiteTokens.JsonOptions;
        var sharedResolver = sharedOptions.TypeInfoResolver;
        var sharedIsReadOnly = sharedOptions.IsReadOnly;
        var path = Path.Combine(inputs.Reports, SiteTokens.SmokeProfile, SiteTokens.ReportFile);
        var reportBytes = await File.ReadAllBytesAsync(path, token);
        var firstContext = SiteReportJsonContext.Create();
        var secondContext = SiteReportJsonContext.Create();
        var firstReport = JsonSerializer.Deserialize(reportBytes, firstContext.SiteReport) ?? throw new JsonException();
        var secondReport = JsonSerializer.Deserialize(reportBytes, secondContext.SiteReport) ?? throw new JsonException();
        await Assert.That(firstReport.SourceRevision).IsEqualTo(inputs.MeasuredRevision);
        await Assert.That(firstReport.Targets.Count).IsEqualTo(SiteTokens.HistoricalTargetCount);
        await Assert.That(secondReport.SourceRevision).IsEqualTo(inputs.MeasuredRevision);
        await Assert.That(secondReport.Targets.Count).IsEqualTo(SiteTokens.HistoricalTargetCount);
        await Assert.That(ReferenceEquals(firstContext.Options, sharedOptions)).IsFalse();
        await Assert.That(ReferenceEquals(firstContext.Options, secondContext.Options)).IsFalse();
        await Assert.That(firstContext.Options.PropertyNameCaseInsensitive).IsEqualTo(sharedOptions.PropertyNameCaseInsensitive);
        await Assert.That(firstContext.Options.WriteIndented).IsEqualTo(sharedOptions.WriteIndented);
        await Assert.That(firstContext.Options.PropertyNamingPolicy?.ConvertName(nameof(SiteReport.SourceRevision)))
            .IsEqualTo(SiteTokens.SourceRevision);

        var after = await SiteNodeProbe.RunAsync(inputs, request, token);
        await Assert.That(after.Value.GetDouble()).IsEqualTo(5d);
        await Assert.That(ReferenceEquals(sharedOptions.TypeInfoResolver, sharedResolver)).IsTrue();
        await Assert.That(sharedOptions.IsReadOnly).IsEqualTo(sharedIsReadOnly);
    }

    [Test]
    public async Task AC_CQ_013_GeneratedContextPreservesOmittedRootDtoDefaults()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        var source = await ReadAuthenticSmokeReport(inputs, token);
        var omitted = source.DeepClone().AsObject();
        omitted.Remove(SiteTokens.SourceRevision);
        omitted.Remove(SiteTokens.DatasetSha256);
        omitted.Remove(SiteTokens.Options);
        omitted.Remove(SiteTokens.Targets);
        omitted.Remove(SiteTokens.Cases);

        var (reflection, generated) = DeserializeBoth(omitted.ToJsonString());
        await AssertReflectionParity(reflection, generated);
        await Assert.That(generated.SourceRevision).IsEqualTo(string.Empty);
        await Assert.That(generated.DatasetSha256).IsEqualTo(string.Empty);
        await Assert.That(generated.Options.Operations).IsEqualTo(SiteTokens.Zero);
        await Assert.That(generated.Options.Repetitions).IsEqualTo(SiteTokens.Zero);
        await Assert.That(generated.Targets.Count).IsEqualTo(SiteTokens.Zero);
        await Assert.That(generated.Cases.Count).IsEqualTo(SiteTokens.Zero);
    }

    [Test]
    public async Task AC_CQ_013_GeneratedContextPreservesOmittedNestedDtoDefaults()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        var omitted = (await ReadAuthenticSmokeReport(inputs, token)).DeepClone().AsObject();
        omitted[SiteTokens.Targets]!.AsArray()[SiteTokens.Zero]!.AsObject().Remove(SiteTokens.Name);
        var caseIndex = FirstMeasuredCaseIndex(omitted);
        var reportCase = omitted[SiteTokens.Cases]!.AsArray()[caseIndex]!.AsObject();
        reportCase.Remove(SiteTokens.Target);
        reportCase.Remove(SiteTokens.Scenario);
        reportCase.Remove(SiteTokens.Status);
        reportCase[SiteTokens.Measurement]!.AsObject().Remove(SiteTokens.Latency);

        var (reflection, generated) = DeserializeBoth(omitted.ToJsonString());
        await AssertReflectionParity(reflection, generated);
        await Assert.That(generated.Targets[SiteTokens.Zero].Name).IsEqualTo(string.Empty);
        await Assert.That(generated.Cases[caseIndex].Target).IsEqualTo(string.Empty);
        await Assert.That(generated.Cases[caseIndex].Scenario).IsEqualTo(string.Empty);
        await Assert.That(generated.Cases[caseIndex].Status).IsEqualTo(string.Empty);
        await Assert.That(generated.Cases[caseIndex].Measurement!.Latency.P50Ms).IsEqualTo(SiteTokens.Zero);
    }

    [Test]
    public async Task AC_CQ_013_GeneratedContextMatchesReflectionForExplicitNullRootReferences()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        var source = await ReadAuthenticSmokeReport(inputs, token);
        var rootNulls = source.DeepClone().AsObject();
        rootNulls[SiteTokens.SourceRevision] = null;
        rootNulls[SiteTokens.DatasetSha256] = null;
        rootNulls[SiteTokens.Options] = null;
        rootNulls[SiteTokens.Targets] = null;
        rootNulls[SiteTokens.Cases] = null;
        var (rootReflection, rootGenerated) = DeserializeBoth(rootNulls.ToJsonString());
        await AssertReflectionParity(rootReflection, rootGenerated);
        await Assert.That(rootGenerated.SourceRevision is null).IsTrue();
        await Assert.That(rootGenerated.DatasetSha256 is null).IsTrue();
        await Assert.That(rootGenerated.Options is null).IsTrue();
        await Assert.That(rootGenerated.Targets is null).IsTrue();
        await Assert.That(rootGenerated.Cases is null).IsTrue();
    }

    [Test]
    public async Task AC_CQ_013_GeneratedContextMatchesReflectionForExplicitNullCaseReferences()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        var source = await ReadAuthenticSmokeReport(inputs, token);
        var nestedNulls = source.DeepClone().AsObject();
        nestedNulls[SiteTokens.Targets]!.AsArray()[SiteTokens.Zero]!.AsObject()[SiteTokens.Name] = null;
        var caseIndex = FirstMeasuredCaseIndex(nestedNulls);
        var reportCase = nestedNulls[SiteTokens.Cases]!.AsArray()[caseIndex]!.AsObject();
        reportCase[SiteTokens.Target] = null;
        reportCase[SiteTokens.Scenario] = null;
        reportCase[SiteTokens.Status] = null;
        reportCase[SiteTokens.Detail] = null;
        reportCase[SiteTokens.Measurement] = null;

        var (nestedReflection, nestedGenerated) = DeserializeBoth(nestedNulls.ToJsonString());
        await AssertReflectionParity(nestedReflection, nestedGenerated);
        await Assert.That(nestedGenerated.Targets[SiteTokens.Zero].Name is null).IsTrue();
        await Assert.That(nestedGenerated.Cases[caseIndex].Target is null).IsTrue();
        await Assert.That(nestedGenerated.Cases[caseIndex].Scenario is null).IsTrue();
        await Assert.That(nestedGenerated.Cases[caseIndex].Status is null).IsTrue();
        await Assert.That(nestedGenerated.Cases[caseIndex].Detail is null).IsTrue();
        await Assert.That(nestedGenerated.Cases[caseIndex].Measurement is null).IsTrue();
    }

    [Test]
    public async Task AC_CQ_013_GeneratedContextMatchesReflectionForExplicitNullMeasurementReferences()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        var nestedNulls = (await ReadAuthenticSmokeReport(inputs, token)).DeepClone().AsObject();
        var measurementIndex = FirstMeasuredCaseIndex(nestedNulls);
        var measurement = nestedNulls[SiteTokens.Cases]!.AsArray()[measurementIndex]![SiteTokens.Measurement]!.AsObject();
        measurement[SiteTokens.Latency] = null;
        measurement[SiteTokens.Enqueue] = null;
        measurement[SiteTokens.Receive] = null;
        measurement[SiteTokens.Ack] = null;
        measurement[SiteTokens.ClientResources] = null;

        var (nestedReflection, nestedGenerated) = DeserializeBoth(nestedNulls.ToJsonString());
        await AssertReflectionParity(nestedReflection, nestedGenerated);
        await Assert.That(nestedGenerated.Cases[measurementIndex].Measurement!.Latency is null).IsTrue();
        await Assert.That(nestedGenerated.Cases[measurementIndex].Measurement!.Enqueue is null).IsTrue();
        await Assert.That(nestedGenerated.Cases[measurementIndex].Measurement!.Receive is null).IsTrue();
        await Assert.That(nestedGenerated.Cases[measurementIndex].Measurement!.Ack is null).IsTrue();
        await Assert.That(nestedGenerated.Cases[measurementIndex].Measurement!.ClientResources is null).IsTrue();
    }

    private static async Task<JsonObject> ReadAuthenticSmokeReport(SiteTestInputs inputs, CancellationToken token)
    {
        var path = Path.Combine(inputs.Reports, SiteTokens.SmokeProfile, SiteTokens.ReportFile);
        var bytes = await File.ReadAllBytesAsync(path, token);
        return JsonNode.Parse(bytes)?.AsObject() ?? throw new JsonException();
    }

    private static (SiteReport Reflection, SiteReport Generated) DeserializeBoth(string json)
    {
        var reflection = JsonSerializer.Deserialize<SiteReport>(json, SiteTokens.JsonOptions) ?? throw new JsonException();
        var generated = JsonSerializer.Deserialize(json, SiteReportJsonContext.Create().SiteReport) ?? throw new JsonException();
        return (reflection, generated);
    }

    private static async Task AssertReflectionParity(SiteReport reflection, SiteReport generated)
    {
        var reflectionJson = JsonSerializer.SerializeToElement(reflection, SiteTokens.JsonOptions);
        var generatedJson = JsonSerializer.SerializeToElement(generated, SiteTokens.JsonOptions);
        await Assert.That(JsonElement.DeepEquals(reflectionJson, generatedJson)).IsTrue();
    }

    private static int FirstMeasuredCaseIndex(JsonObject report)
    {
        var cases = report[SiteTokens.Cases]!.AsArray();
        return Enumerable.Range(SiteTokens.Zero, cases.Count)
            .First(index => cases[index]?[SiteTokens.Measurement] is JsonObject);
    }
}
