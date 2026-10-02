using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-009: direct system-clock properties are separated from clock abstractions.</summary>
internal sealed class SystemClockSemanticTests
{
    private const string DiagnosticId = SystemClockAccessAnalyzer.DiagnosticId;

    [Test]
    public async Task DateTimeAndDateTimeOffsetClockPropertiesAreDiagnosedAsync()
    {
        const string dateNow = "DateClock.Now";
        const string dateToday = "Today";
        const string dateUtcNow = "UtcNow";
        const string offsetNow = "OffsetClock.Now";
        const string offsetUtcNow = "OffsetClock.UtcNow";
        const string source = """
            using DateClock = System.DateTime;
            using OffsetClock = System.DateTimeOffset;
            using static System.DateTime;
            internal static class Subject
            {
                internal static DateClock First() => DateClock.Now;
                internal static DateClock Second() => Today;
                internal static DateClock Third() => UtcNow;
                internal static OffsetClock Fourth() => OffsetClock.Now;
                internal static OffsetClock Fifth() => OffsetClock.UtcNow;
            }
            """;

        var findings = await AnalyzerFixture.AnalyzeAsync(new SystemClockAccessAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, dateNow, dateToday, dateUtcNow, offsetNow, offsetUtcNow);
    }

    [Test]
    public async Task TimeProviderAndNonClockDatePropertiesAreAllowedAsync()
    {
        const string source = """
            using System;
            internal sealed class Subject(TimeProvider timeProvider, ClockData clockData)
            {
                internal DateTimeOffset ProviderNow() => timeProvider.GetUtcNow();
                internal static TimeProvider SystemProvider() => TimeProvider.System;
                internal static DateTime Minimum() => DateTime.MinValue;
                internal static DateTimeOffset Maximum() => DateTimeOffset.MaxValue;
                internal DateTime InstanceNow() => clockData.Now;
            }
            internal sealed class ClockData
            {
                internal DateTime Now { get; init; }
            }
            """;

        var findings = await AnalyzerFixture.AnalyzeAsync(new SystemClockAccessAnalyzer(), source);
        await Assert.That(findings).IsEmpty();
    }

    private static async Task AssertFindingsAtTokensAsync(
        ImmutableArray<Diagnostic> findings,
        string source,
        params string[] expectedTokens)
    {
        await Assert.That(findings.Length).IsEqualTo(expectedTokens.Length);
        foreach (var token in expectedTokens)
        {
            var start = source.IndexOf(token, StringComparison.Ordinal);
            await Assert.That(start >= 0).IsTrue();
            var finding = findings.Single(item => item.Location.SourceSpan.Start == start);
            await Assert.That(finding.Id).IsEqualTo(DiagnosticId);
            await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
            await Assert.That(finding.Location.IsInSource).IsTrue();
            await Assert.That(finding.Location.SourceTree?.FilePath).IsEqualTo(AnalyzerFixture.ContractPath);
            await Assert.That(finding.Location.SourceSpan.Length).IsEqualTo(token.Length);
        }
    }
}
