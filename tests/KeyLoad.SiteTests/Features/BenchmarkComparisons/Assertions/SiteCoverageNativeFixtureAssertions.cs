using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteCoverageNativeFixtureAssertions
{
    private const string ChooseFunctionName = "choose";
    private const string NeverCalledFunctionName = "neverCalled";
    private const string NestedFunctionName = "lazy";

    public static async Task AssertPhysicalFunctionsAreDistinct(SiteCoverageNativeReceipt receipt, string root)
    {
        var script = SiteCoverageNativeTestSupport.FindFixtureScript(JsonNode.Parse(receipt.Bytes)!.AsObject(), root);
        var chooseSpan = FunctionSpan(FindFixtureFunction(script, ChooseFunctionName));
        var neverCalledSpan = FunctionSpan(FindFixtureFunction(script, NeverCalledFunctionName));
        var nestedSpan = FunctionSpan(FindFixtureFunction(script, NestedFunctionName));
        var hasNestedFunctionInExecutedBranch = script[SiteCoverageTokens.Functions]!.AsArray()
            .Select(node => FunctionSpan(node!.AsObject())).Any(span => span.StartOffset > chooseSpan.StartOffset &&
                span.EndOffset < chooseSpan.EndOffset);
        await Assert.That(chooseSpan.EndOffset < neverCalledSpan.StartOffset).IsTrue();
        await Assert.That(chooseSpan.StartOffset < nestedSpan.StartOffset).IsTrue();
        await Assert.That(nestedSpan.EndOffset < chooseSpan.EndOffset).IsTrue();
        await Assert.That(hasNestedFunctionInExecutedBranch).IsTrue();
    }

    public static async Task AssertRealNodeOmittedBranchInference(SiteCoverageNativeReceipt first,
        SiteCoverageNativeReceipt second, string root)
    {
        var firstFunction = FindFixtureFunction(SiteCoverageNativeTestSupport.FindFixtureScript(JsonNode.Parse(first.Bytes)!.AsObject(), root),
            ChooseFunctionName);
        var secondFunction = FindFixtureFunction(SiteCoverageNativeTestSupport.FindFixtureScript(JsonNode.Parse(second.Bytes)!.AsObject(), root),
            ChooseFunctionName);
        var firstRanges = ReadRanges(firstFunction);
        var secondRanges = ReadRanges(secondFunction);
        var observed = firstRanges.Skip(SiteCoverageTokens.One).Concat(secondRanges.Skip(SiteCoverageTokens.One))
            .DistinctBy(range => (range.StartOffset, range.EndOffset)).ToArray();
        var reproducedOmission = observed.Any(branch =>
            ExplicitCount(firstRanges, branch) == SiteCoverageTokens.Zero &&
            IsImplicitlyCovered(secondRanges, branch) ||
            ExplicitCount(secondRanges, branch) == SiteCoverageTokens.Zero &&
            IsImplicitlyCovered(firstRanges, branch));
        await Assert.That(reproducedOmission).IsTrue();
    }

    public static Task AssertMalformedRangesRejected(SiteCoverageNativeReceipt receipt, string root,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources)
    {
        var malformed = JsonNode.Parse(receipt.Bytes)!.AsObject();
        var function = FindFixtureFunction(SiteCoverageNativeTestSupport.FindFixtureScript(malformed, root), ChooseFunctionName);
        var rootRange = function[SiteCoverageTokens.Ranges]![SiteCoverageTokens.Zero]!.AsObject();
        rootRange[SiteCoverageTokens.EndOffset] = SiteCoverageNativeTestSupport.FixtureSource.Length +
            SiteCoverageTokens.One;
        return AssertInvalidNativeFixture(malformed, receipt, root, sources);
    }

    public static Task AssertCrossingRangesRejected(SiteCoverageNativeReceipt receipt, string root,
        IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources)
    {
        var malformed = JsonNode.Parse(receipt.Bytes)!.AsObject();
        var function = FindFixtureFunction(SiteCoverageNativeTestSupport.FindFixtureScript(malformed, root), ChooseFunctionName);
        function[SiteCoverageTokens.IsBlockCoverage] = true;
        var rootRange = function[SiteCoverageTokens.Ranges]![SiteCoverageTokens.Zero]!;
        var rootStart = rootRange[SiteCoverageTokens.StartOffset]!.GetValue<int>();
        var rootEnd = rootRange[SiteCoverageTokens.EndOffset]!.GetValue<int>();
        if (rootEnd - rootStart <= SiteCoverageTokens.TwentyFive)
        {
            throw new InvalidOperationException(SiteCoverageTokens.InvalidCoverageFailure);
        }

        function[SiteCoverageTokens.Ranges] = new JsonArray(
            CreateRange(rootStart, rootEnd, SiteCoverageTokens.Zero),
            CreateRange(rootStart + SiteCoverageTokens.Ten, rootStart + SiteCoverageTokens.Twenty, SiteCoverageTokens.One),
            CreateRange(rootStart + SiteCoverageTokens.Fifteen, rootStart + SiteCoverageTokens.TwentyFive, SiteCoverageTokens.One));
        return AssertInvalidNativeFixture(malformed, receipt, root, sources);
    }

    private static async Task AssertInvalidNativeFixture(JsonObject malformed, SiteCoverageNativeReceipt receipt,
        string root, IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources)
    {
        using var document = JsonDocument.Parse(malformed.ToJsonString());
        var rejected = false;
        try
        {
            SiteCoverageNativeRanges.ParseScripts(document.RootElement.GetProperty(SiteCoverageTokens.Result),
                SiteCoverageTokens.NodeRuntime, root, sources, null, receipt.Identity, receipt.Sha256);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        await Assert.That(rejected).IsTrue();
    }

    private static JsonObject FindFixtureFunction(JsonObject script, string name) =>
        script[SiteCoverageTokens.Functions]!.AsArray().Select(node => node!.AsObject()).Single(function =>
            function[SiteCoverageTokens.FunctionName]!.GetValue<string>() == name);

    private static SiteCoverageRange FunctionSpan(JsonObject function)
    {
        var root = function[SiteCoverageTokens.Ranges]![SiteCoverageTokens.Zero]!;
        return new(root[SiteCoverageTokens.StartOffset]!.GetValue<int>(),
            root[SiteCoverageTokens.EndOffset]!.GetValue<int>(), root[SiteCoverageTokens.Count]!.GetValue<long>());
    }

    private static SiteCoverageRange[] ReadRanges(JsonObject function) =>
        function[SiteCoverageTokens.Ranges]!.AsArray().Select(node =>
        {
            var range = node!.AsObject();
            return new SiteCoverageRange(range[SiteCoverageTokens.StartOffset]!.GetValue<int>(),
                range[SiteCoverageTokens.EndOffset]!.GetValue<int>(), range[SiteCoverageTokens.Count]!.GetValue<long>());
        }).ToArray();

    private static long? ExplicitCount(IEnumerable<SiteCoverageRange> ranges, SiteCoverageRange branch) =>
        ranges.Where(range => range.StartOffset == branch.StartOffset && range.EndOffset == branch.EndOffset)
            .Select(range => (long?)range.Count).SingleOrDefault();

    private static bool IsImplicitlyCovered(IReadOnlyList<SiteCoverageRange> ranges, SiteCoverageRange branch)
    {
        if (ExplicitCount(ranges, branch) is not null)
        {
            return false;
        }

        var boundaries = ranges.SelectMany(range => new[] { range.StartOffset, range.EndOffset })
            .Where(offset => offset > branch.StartOffset && offset < branch.EndOffset)
            .Append(branch.StartOffset).Append(branch.EndOffset).Distinct().Order().ToArray();
        return boundaries.Zip(boundaries.Skip(SiteCoverageTokens.One), (start, _) => IsCoveredAt(ranges, start))
            .All(covered => covered);
    }

    private static bool IsCoveredAt(IReadOnlyList<SiteCoverageRange> ranges, int offset) =>
        ranges.Where(range => range.StartOffset <= offset && offset < range.EndOffset)
            .MinBy(range => range.EndOffset - range.StartOffset) is { Count: > SiteCoverageTokens.Zero };

    private static JsonObject CreateRange(int start, int end, int count) => new()
    {
        [SiteCoverageTokens.StartOffset] = start,
        [SiteCoverageTokens.EndOffset] = end,
        [SiteCoverageTokens.Count] = count,
    };
}
