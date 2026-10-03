using System.Net;
using System.Text.RegularExpressions;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed partial class SiteDatabaseCompositionTests
{
    [Test]
    public async Task AC_COMP_001_PublicIntroductionsDescribeOneLinkedDatabaseWithFamiliarSql()
    {
        var readme = await ReadSourceAsync(SiteDatabaseCompositionTokens.ReadmePath);
        var html = await ReadSourceAsync(SiteDatabaseCompositionTokens.HtmlPath);
        var hero = ReadSection(html, SiteDatabaseCompositionTokens.HeroStart,
            SiteDatabaseCompositionTokens.ParagraphEnd);
        var readmeIntroduction = ReadSection(readme, SiteDatabaseCompositionTokens.ReadmeIntroductionStart,
            SiteDatabaseCompositionTokens.ReadmeIntroductionEnd);
        foreach (var introduction in new[] { readmeIntroduction, hero })
        {
            await AssertIncludesAsync(introduction, SiteDatabaseCompositionTokens.Models);
            await AssertIncludesAsync(introduction,
                [SiteDatabaseCompositionTokens.OneDatabase, SiteDatabaseCompositionTokens.CanonicalLinks,
                    SiteDatabaseCompositionTokens.FamiliarSql]);
        }
    }

    [Test]
    public async Task AC_COMP_001_PublicCompositionExplainsQueueGraphAndReverseFlows()
    {
        var sections = await ReadCompositionSectionsAsync();
        foreach (var section in sections)
        {
            await AssertIncludesAsync(section, SiteDatabaseCompositionTokens.Flows);
        }
    }

    [Test]
    public async Task AC_COMP_001_008_PublicCompositionRetainsCurrentStageAndAtomicBoundaries()
    {
        var sections = await ReadCompositionSectionsAsync();
        await AssertIncludesAsync(sections[SiteDatabaseCompositionTokens.ReadmeSectionIndex],
            SiteDatabaseCompositionTokens.ReadmeStageContracts);
        await AssertIncludesAsync(sections[SiteDatabaseCompositionTokens.HtmlSectionIndex],
            SiteDatabaseCompositionTokens.StageContracts);
    }

    [Test]
    public async Task AC_COMP_001_PublicCompositionLinksItsFeatureAndArchitectureContract()
    {
        var readme = await ReadSourceAsync(SiteDatabaseCompositionTokens.ReadmePath);
        var html = await ReadSourceAsync(SiteDatabaseCompositionTokens.HtmlPath);
        await AssertIncludesAsync(readme,
            [SiteDatabaseCompositionTokens.FeaturePath, SiteDatabaseCompositionTokens.AdrPath]);
        await Assert.That(html).Contains(SiteDatabaseCompositionTokens.GitHubRoot + SiteDatabaseCompositionTokens.FeaturePath);
        await Assert.That(html).Contains(SiteDatabaseCompositionTokens.GitHubRoot + SiteDatabaseCompositionTokens.AdrPath);
    }

    private static async Task<string[]> ReadCompositionSectionsAsync()
    {
        var readme = await ReadSourceAsync(SiteDatabaseCompositionTokens.ReadmePath);
        var html = await ReadSourceAsync(SiteDatabaseCompositionTokens.HtmlPath);
        return
        [
            ReadSection(readme, SiteDatabaseCompositionTokens.ReadmeCompositionStart,
                SiteDatabaseCompositionTokens.ReadmeCompositionEnd),
            ReadSection(html, SiteDatabaseCompositionTokens.HtmlCompositionStart,
                SiteDatabaseCompositionTokens.HtmlSectionEnd),
        ];
    }

    private static Task<string> ReadSourceAsync(string relativePath)
    {
        var repository = Environment.GetEnvironmentVariable(SiteTokens.RepositoryEnvironment);
        if (string.IsNullOrWhiteSpace(repository) || !Path.IsPathFullyQualified(repository))
        {
            throw new InvalidOperationException(SiteDatabaseCompositionTokens.MissingRepository);
        }

        return File.ReadAllTextAsync(Path.Combine(repository, relativePath),
            TestContext.Current!.Execution.CancellationToken);
    }

    private static string ReadSection(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = start < 0 ? -1 : source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            throw new InvalidDataException(SiteDatabaseCompositionTokens.MissingSection);
        }

        return source[start..end];
    }

    private static async Task AssertIncludesAsync(string source, IEnumerable<string> contracts)
    {
        var plainText = WebUtility.HtmlDecode(HtmlTags().Replace(source, SiteDatabaseCompositionTokens.WordSeparator));
        var text = string.Join(SiteDatabaseCompositionTokens.WordSeparator,
            plainText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        foreach (var contract in contracts)
        {
            await Assert.That(text.Contains(contract, StringComparison.OrdinalIgnoreCase)).IsTrue();
        }
    }

    [GeneratedRegex(SiteDatabaseCompositionTokens.HtmlTagPattern, RegexOptions.CultureInvariant,
        SiteDatabaseCompositionTokens.PatternTimeoutMilliseconds)]
    private static partial Regex HtmlTags();
}
