using System.Text.RegularExpressions;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteProductThesisTests
{
    [Test]
    public async Task AC_COMP_009_ReadmeStatesEveryAiNativeThesisAsItsOwnSection()
    {
        var readme = await SiteProductCopy.ReadSourceAsync(SiteDatabaseCompositionTokens.ReadmePath);
        await Assert.That(SiteProductCopy.Missing(readme, SiteProductThesisTokens.ReadmeTheses)).IsEmpty();
        await Assert.That(HasForbiddenClaim(readme)).IsFalse();
    }

    [Test]
    public async Task AC_COMP_009_LandingStatesTheSameThesesInItsVisibleCopy()
    {
        var html = await SiteProductCopy.ReadSourceAsync(SiteDatabaseCompositionTokens.HtmlPath);
        var main = SiteProductCopy.ReadSection(html, SiteProductThesisTokens.LandingMainStart,
            SiteProductThesisTokens.LandingMainEnd);
        await Assert.That(SiteProductCopy.Missing(main, SiteProductThesisTokens.Theses)).IsEmpty();
        await Assert.That(HasForbiddenClaim(html)).IsFalse();
    }

    [Test]
    public async Task AC_COMP_009_CopyWithoutTheThesesIsReportedMissing()
    {
        var missing = SiteProductCopy.Missing(SiteProductThesisTokens.UnrelatedCopy, SiteProductThesisTokens.Theses);
        await Assert.That(missing).IsEquivalentTo(SiteProductThesisTokens.Theses);
    }

    [Test]
    public async Task AC_COMP_009_CopyMissingOneThesisReportsOnlyThatThesis()
    {
        var missing = SiteProductCopy.Missing(SiteProductThesisTokens.PartialCopy, SiteProductThesisTokens.Theses);
        await Assert.That(missing).IsEquivalentTo(new[] { SiteProductThesisTokens.StackRationale });
    }

    private static bool HasForbiddenClaim(string source) => Regex.IsMatch(source, SiteMetadataTokens.ForbiddenClaims,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
