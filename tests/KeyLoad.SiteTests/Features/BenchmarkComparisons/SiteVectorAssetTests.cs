using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteVectorAssetTests
{
    [Test]
    public async Task AC_VEC_001_CommittedPosterIsAccessibleVectorArtworkWithAllModels()
    {
        var inputs = SiteTestInputs.Read();
        var posterPath = Path.Combine(inputs.Repository, SiteVectorAssetTokens.PosterRelativePath);
        XDocument poster;
        try
        {
            poster = XDocument.Load(posterPath, LoadOptions.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            throw new InvalidDataException(SiteVectorAssetTokens.MissingPosterFailure, exception);
        }

        var root = poster.Root;
        await Assert.That(root?.Name.LocalName).IsEqualTo("svg");
        await Assert.That(root?.Name.NamespaceName).IsEqualTo(SiteVectorAssetTokens.SvgNamespace);
        await Assert.That(poster.Descendants().Any(element => element.Name.LocalName is "path" or "rect" or "circle" or "polygon" or "text")).IsTrue();
        await AssertSafeVector(poster);
        await AssertModelLabels(poster);
    }

    [Test]
    public async Task AC_VEC_002_PageUsesOneCanonicalLiveSvgMarkAndKeepsFaviconAtSiteRoot()
    {
        var inputs = SiteTestInputs.Read();
        var htmlPath = Path.Combine(inputs.Repository, SiteVectorAssetTokens.IndexRelativePath);
        var faviconPath = Path.Combine(inputs.Repository, SiteVectorAssetTokens.FaviconRelativePath);
        await Assert.That(File.Exists(faviconPath)).IsTrue();
        var favicon = XDocument.Load(faviconPath, LoadOptions.None);
        await Assert.That(favicon.Root?.Name.LocalName).IsEqualTo("svg");

        var html = await File.ReadAllTextAsync(htmlPath, Encoding.UTF8);
        var images = SiteVectorAssetHtml.Images(html);
        var sceneMarks = images.Where(image => image.Classes.Contains(SiteVectorAssetTokens.LiveMarkClass,
            StringComparer.Ordinal)).ToArray();
        await Assert.That(sceneMarks.Length).IsEqualTo(1);
        await Assert.That(sceneMarks[0].Source).IsEqualTo(SiteVectorAssetTokens.CanonicalFaviconUrl);
        await Assert.That(html.Contains(SiteVectorAssetTokens.SceneFallbackDescription, StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertSafeVector(XDocument poster)
    {
        var elements = poster.Descendants().ToArray();
        var hasForbiddenElement = elements.Any(element => element.Name.LocalName.Equals(
            SiteVectorAssetTokens.ImageElement, StringComparison.OrdinalIgnoreCase) ||
            element.Name.LocalName.Equals(SiteVectorAssetTokens.ForeignObjectElement, StringComparison.OrdinalIgnoreCase) ||
            element.Name.LocalName.Equals(SiteVectorAssetTokens.ScriptElement, StringComparison.OrdinalIgnoreCase));
        var hasUnsafeReference = elements.SelectMany(element => element.Attributes()).Any(attribute =>
            IsExternalReference(attribute.Name.LocalName, attribute.Value)) ||
            elements.SelectMany(element => element.Nodes().OfType<XText>()).Any(node =>
                HasExternalAssetReference(node.Value));
        var fullText = poster.ToString(SaveOptions.DisableFormatting);
        await Assert.That(hasForbiddenElement || hasUnsafeReference || ContainsEmbeddedPayload(fullText)).IsFalse();
    }

    private static bool IsExternalReference(string name, string value)
    {
        if (name is not (SiteVectorAssetTokens.HrefAttribute or SiteVectorAssetTokens.XlinkHrefAttribute))
        {
            return false;
        }

        return value.StartsWith(SiteVectorAssetTokens.DataScheme, StringComparison.OrdinalIgnoreCase) ||
            value.Contains(SiteVectorAssetTokens.ExternalScheme, StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(SiteVectorAssetTokens.RasterMimeMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExternalAssetReference(string value) =>
        value.Contains("url(http:", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("url(https:", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("url(//", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("@import", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsEmbeddedPayload(string source) =>
        source.Contains(SiteVectorAssetTokens.DataScheme, StringComparison.OrdinalIgnoreCase) ||
        source.Contains("url(http:", StringComparison.OrdinalIgnoreCase) ||
        source.Contains("url(https:", StringComparison.OrdinalIgnoreCase) ||
        source.Contains("url(//", StringComparison.OrdinalIgnoreCase) ||
        source.Contains("@import", StringComparison.OrdinalIgnoreCase);

    private static async Task AssertModelLabels(XDocument poster)
    {
        var text = string.Join(' ', poster.Descendants().Where(element => element.Name.LocalName == "text")
            .Select(element => element.Value));
        foreach (var label in SiteVectorAssetTokens.RequiredLabels)
        {
            await Assert.That(text.Contains(label, StringComparison.OrdinalIgnoreCase)).IsTrue();
        }
    }
}

internal static class SiteVectorAssetHtml
{
    private static readonly Regex ImageTag = new("<img\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Attribute = new("(?<name>[a-z-]+)\\s*=\\s*[\\\"'](?<value>[^\\\"']*)[\\\"']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<SiteVectorImage> Images(string html)
    {
        return ImageTag.Matches(html).Select(match => Attribute.Matches(match.Value)
            .ToDictionary(attribute => attribute.Groups[SiteVectorAssetTokens.RegexNameGroup].Value,
                attribute => attribute.Groups[SiteVectorAssetTokens.RegexValueGroup].Value, StringComparer.OrdinalIgnoreCase))
            .Select(attributes => new SiteVectorImage(
                attributes.GetValueOrDefault(SiteVectorAssetTokens.ClassAttributeName, string.Empty),
                attributes.GetValueOrDefault(SiteVectorAssetTokens.SourceAttributeName, string.Empty)))
            .ToArray();
    }
}

internal sealed record SiteVectorImage(string Class, string Source)
{
    public IReadOnlyList<string> Classes => Class.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
