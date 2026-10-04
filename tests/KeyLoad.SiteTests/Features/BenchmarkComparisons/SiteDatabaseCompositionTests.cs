namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteDatabaseCompositionTests
{
    [Test]
    public async Task AC_COMP_001_PublicIntroductionsDescribeOneLinkedDatabaseWithFamiliarSql()
    {
        var readme = await SiteProductCopy.ReadSourceAsync(SiteDatabaseCompositionTokens.ReadmePath);
        var html = await SiteProductCopy.ReadSourceAsync(SiteDatabaseCompositionTokens.HtmlPath);
        var hero = SiteProductCopy.ReadSection(html, SiteDatabaseCompositionTokens.HeroStart,
            SiteDatabaseCompositionTokens.ParagraphEnd);
        var readmeIntroduction = SiteProductCopy.ReadSection(readme, SiteDatabaseCompositionTokens.ReadmeIntroductionStart,
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
        foreach (var section in sections)
        {
            await AssertIncludesAsync(section, SiteDatabaseCompositionTokens.StageContracts);
        }
    }

    [Test]
    public async Task AC_COMP_001_PublicCompositionLinksItsFeatureAndArchitectureContract()
    {
        var readme = await SiteProductCopy.ReadSourceAsync(SiteDatabaseCompositionTokens.ReadmePath);
        var html = await SiteProductCopy.ReadSourceAsync(SiteDatabaseCompositionTokens.HtmlPath);
        await AssertIncludesAsync(readme,
            [SiteDatabaseCompositionTokens.FeaturePath, SiteDatabaseCompositionTokens.AdrPath]);
        await Assert.That(html).Contains(SiteDatabaseCompositionTokens.GitHubRoot + SiteDatabaseCompositionTokens.FeaturePath);
        await Assert.That(html).Contains(SiteDatabaseCompositionTokens.GitHubRoot + SiteDatabaseCompositionTokens.AdrPath);
    }

    private static async Task<string[]> ReadCompositionSectionsAsync()
    {
        var readme = await SiteProductCopy.ReadSourceAsync(SiteDatabaseCompositionTokens.ReadmePath);
        var html = await SiteProductCopy.ReadSourceAsync(SiteDatabaseCompositionTokens.HtmlPath);
        return
        [
            SiteProductCopy.ReadSection(readme, SiteDatabaseCompositionTokens.ReadmeCompositionStart,
                SiteDatabaseCompositionTokens.ReadmeCompositionEnd),
            SiteProductCopy.ReadSection(html, SiteDatabaseCompositionTokens.HtmlCompositionStart,
                SiteDatabaseCompositionTokens.HtmlSectionEnd),
        ];
    }

    private static async Task AssertIncludesAsync(string source, IEnumerable<string> contracts)
    {
        await Assert.That(SiteProductCopy.Missing(source, contracts)).IsEmpty();
    }
}
