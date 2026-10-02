using System.Xml.Linq;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-009 and AC-BC-027: validate native branch spellings and physical source-line bounds.</summary>
internal sealed class SiteAnalyzerCoverageInputSemanticsTests
{
    [Test]
    public async Task NativeBranchBooleanCasingNormalizesBeforeDuplicateChecksAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.NativeBranchBooleanCases);
        var nativeCasing = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(nativeCasing.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        using var nativeReport = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        var nativeModule = nativeReport.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSummaryProperty);
        await Assert.That(nativeModule.GetProperty(SiteAnalyzerCoverageTokens.JsonBranchesValid).GetInt64())
            .IsEqualTo(SiteAnalyzerCoverageTokens.BranchDenominator *
                (SiteAnalyzerCoverageTokens.ExpectedSourceCount - SiteAnalyzerCoverageTokens.ExpectedDeclarationCount));

        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.DuplicateBranchCaseVariant);
        var normalizedDuplicate = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(normalizedDuplicate.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);

        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.MalformedBranchBoolean);
        var malformedBoolean = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(malformedBoolean.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
    }

    [Test]
    public async Task CoberturaLineNumbersMustMatchHashVerifiedPhysicalFileBoundsAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        var zeroFixture = SiteAnalyzerCoverageFixture.ZeroLineNumber;
        var tenthLineMarker = SiteAnalyzerCoverageTokens.LineOpen + SiteAnalyzerCoverageTokens.LinesPerSource
            .ToString(System.Globalization.CultureInfo.InvariantCulture) + SiteAnalyzerCoverageTokens.LineHits;
        await Assert.That(zeroFixture.Contains(SiteAnalyzerCoverageTokens.LineNumberZeroAttribute, StringComparison.Ordinal)).IsTrue();
        await Assert.That(zeroFixture.Contains(tenthLineMarker, StringComparison.Ordinal)).IsTrue();
        await scope.WriteFixtureAsync(zeroFixture);
        var zero = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(zero.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);

        var physicalLines = SiteAnalyzerCoverageXmlBuilder.GetPhysicalLineCount(SiteAnalyzerCoverageTokens.HashFileRelativePath);
        var beyondFixture = SiteAnalyzerCoverageFixture.BeyondPhysicalLine;
        var beyondMarker = SiteAnalyzerCoverageTokens.LineOpen + (physicalLines + SiteAnalyzerCoverageTokens.CounterOne)
            .ToString(System.Globalization.CultureInfo.InvariantCulture) + SiteAnalyzerCoverageTokens.LineHits;
        await Assert.That(beyondFixture.Contains(beyondMarker, StringComparison.Ordinal)).IsTrue();
        var beyondNumbers = ReadTargetLineNumbers(beyondFixture);
        await Assert.That(beyondNumbers.Length).IsEqualTo(SiteAnalyzerCoverageTokens.LinesPerSource);
        await Assert.That(beyondNumbers.Take(SiteAnalyzerCoverageTokens.LinesPerSource - SiteAnalyzerCoverageTokens.CounterOne)
            .All(line => line > 0 && line <= physicalLines)).IsTrue();
        await Assert.That(beyondNumbers[^SiteAnalyzerCoverageTokens.CounterOne]).IsEqualTo(physicalLines + SiteAnalyzerCoverageTokens.CounterOne);
        await scope.WriteFixtureAsync(beyondFixture);
        var beyondEnd = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(beyondEnd.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);

        var lastFixture = SiteAnalyzerCoverageFixture.LastPhysicalLine;
        var lastMarker = SiteAnalyzerCoverageTokens.LineOpen + physicalLines.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + SiteAnalyzerCoverageTokens.LineHits;
        await Assert.That(lastFixture.Contains(lastMarker, StringComparison.Ordinal)).IsTrue();
        var lastNumbers = ReadTargetLineNumbers(lastFixture);
        await Assert.That(lastNumbers.Length).IsEqualTo(SiteAnalyzerCoverageTokens.LinesPerSource);
        await Assert.That(lastNumbers.Take(SiteAnalyzerCoverageTokens.LinesPerSource - SiteAnalyzerCoverageTokens.CounterOne)
            .All(line => line > 0 && line <= physicalLines)).IsTrue();
        await Assert.That(lastNumbers[^SiteAnalyzerCoverageTokens.CounterOne]).IsEqualTo(physicalLines);
        await scope.WriteFixtureAsync(lastFixture);
        var lastLine = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(lastLine.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        using var report = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        await Assert.That(report.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSummaryProperty)
            .GetProperty(SiteAnalyzerCoverageTokens.JsonLinesValid).GetInt64())
            .IsEqualTo(SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount);
        var targetFile = report.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonFiles).EnumerateArray()
            .Single(file => file.GetProperty(SiteAnalyzerCoverageTokens.PathProperty).GetString() == SiteAnalyzerCoverageTokens.HashFileRelativePath);
        await Assert.That(targetFile.GetProperty(SiteAnalyzerCoverageTokens.JsonLinesValid).GetInt64()).IsEqualTo(SiteAnalyzerCoverageTokens.LinesPerSource);
        await Assert.That(targetFile.GetProperty(SiteAnalyzerCoverageTokens.JsonLinesCovered).GetInt64()).IsEqualTo(SiteAnalyzerCoverageTokens.LinesPerSource);
    }

    private static int[] ReadTargetLineNumbers(string fixture) => XDocument.Parse(fixture)
        .Descendants(SiteAnalyzerCoverageTokens.XmlClassElement)
        .Single(element => element.Attribute(SiteAnalyzerCoverageTokens.XmlFilenameAttribute)?.Value == SiteAnalyzerCoverageTokens.HashFileRelativePath)
        .Descendants(SiteAnalyzerCoverageTokens.XmlLineElement)
        .Select(line => int.Parse(line.Attribute(SiteAnalyzerCoverageTokens.XmlNumberAttribute)!.Value, System.Globalization.CultureInfo.InvariantCulture))
        .ToArray();
}
