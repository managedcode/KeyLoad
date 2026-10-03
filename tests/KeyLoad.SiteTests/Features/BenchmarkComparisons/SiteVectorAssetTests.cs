using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteVectorAssetTests
{
    [Test]
    public async Task AC_VEC_001_CommittedPosterIsAccessibleVectorArtworkWithAllModels()
    {
        var inputs = SiteTestInputs.Read();
        foreach (var posterPath in SiteVectorAssetSourceTokens.PosterPaths)
        {
            await VerifyPoster(Path.Combine(inputs.Repository, posterPath), inputs.Repository);
        }
    }

    [Test]
    public async Task AC_VEC_002_PageUsesOneCanonicalLiveSvgMarkAndKeepsFaviconAtSiteRoot()
    {
        var inputs = SiteTestInputs.Read();
        var htmlPath = Path.Combine(inputs.Repository, SiteVectorAssetTokens.IndexRelativePath);
        var faviconPath = Path.Combine(inputs.Repository, SiteVectorAssetTokens.FaviconRelativePath);
        await Assert.That(File.Exists(faviconPath)).IsTrue();
        var favicon = XDocument.Load(faviconPath, LoadOptions.None);
        await Assert.That(favicon.Root?.Name.LocalName).IsEqualTo(SiteVectorAssetSourceTokens.SvgElement);

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
        var elements = poster.Root!.DescendantsAndSelf().ToArray();
        var identifiers = elements.SelectMany(element => element.Attributes()).Where(attribute =>
                attribute.Name.LocalName == SiteVectorAssetSourceTokens.IdAttribute)
            .GroupBy(attribute => attribute.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        await Assert.That(HasForbiddenElement(elements) || ContainsEmbeddedPayload(poster)).IsFalse();
        if (!HasOnlyLocalReferences(elements, identifiers))
        {
            throw new InvalidDataException(SiteVectorAssetSourceTokens.QueryReferenceFailure);
        }
    }

    private static bool HasForbiddenElement(IEnumerable<XElement> elements) => elements.Any(element =>
        element.Name.LocalName.Equals(SiteVectorAssetSourceTokens.ImageElement, StringComparison.OrdinalIgnoreCase) ||
        element.Name.LocalName.Equals(SiteVectorAssetSourceTokens.ForeignObjectElement, StringComparison.OrdinalIgnoreCase) ||
        element.Name.LocalName.Equals(SiteVectorAssetSourceTokens.ScriptElement, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsEmbeddedPayload(XDocument poster)
    {
        var source = poster.ToString(SaveOptions.DisableFormatting);
        return source.Contains(SiteVectorAssetSourceTokens.DataScheme, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasOnlyLocalReferences(XElement[] elements,
        IReadOnlyDictionary<string, int> identifiers)
    {
        var attributes = elements.SelectMany(element => element.Attributes()).Where(attribute => !attribute.IsNamespaceDeclaration).ToArray();
        var values = attributes.Select(attribute => attribute.Value).Concat(elements
            .Where(element => element.Name.LocalName == SiteVectorAssetSourceTokens.StyleElement)
            .SelectMany(element => element.Nodes().OfType<XText>()).Select(node => node.Value));
        foreach (var attribute in attributes.Where(attribute => attribute.Name.LocalName == SiteVectorAssetSourceTokens.HrefAttribute))
        {
            if (!HasUniqueFragment(attribute.Value, identifiers))
            {
                return false;
            }
        }

        return values.All(value => HasOnlyLocalUrlFunctions(value, identifiers) && !HasExternalReference(value));
    }

    private static bool HasOnlyLocalUrlFunctions(string value, IReadOnlyDictionary<string, int> identifiers)
    {
        var references = SiteVectorAssetSourceTokens.SvgUrlReference.Matches(value);
        var referenceCount = SiteVectorAssetSourceTokens.UrlFunction.Count(value);
        return references.Count == referenceCount && references.All(reference => HasUniqueFragment(
            SiteVectorAssetSourceTokens.FragmentPrefix + reference.Groups[SiteVectorAssetSourceTokens.FragmentGroup].Value,
            identifiers));
    }

    private static bool HasUniqueFragment(string value, IReadOnlyDictionary<string, int> identifiers)
    {
        var match = SiteVectorAssetSourceTokens.SvgHrefFragment.Match(value);
        return match.Success && identifiers.GetValueOrDefault(
            match.Groups[SiteVectorAssetSourceTokens.FragmentGroup].Value) == SiteTokens.One;
    }

    private static bool HasExternalReference(string value) =>
        value.Contains(SiteVectorAssetSourceTokens.ExternalScheme, StringComparison.OrdinalIgnoreCase) ||
        value.TrimStart().StartsWith(SiteVectorAssetSourceTokens.ProtocolRelativePrefix, StringComparison.Ordinal) ||
        value.Contains(SiteVectorAssetSourceTokens.ImportRuleMarker, StringComparison.OrdinalIgnoreCase);

    private static bool HasVectorGeometry(XDocument poster) => poster.Descendants().Any(element =>
        element.Name.LocalName is SiteVectorAssetSourceTokens.PathElement or SiteVectorAssetSourceTokens.RectElement or
            SiteVectorAssetSourceTokens.CircleElement or SiteVectorAssetSourceTokens.PolygonElement or
            SiteVectorAssetSourceTokens.TextElement);

    private static async Task AssertModelLabels(XDocument poster)
    {
        var text = string.Join(SiteVectorAssetSourceTokens.TextJoinSeparator,
            poster.Descendants().Where(element => element.Name.LocalName == SiteVectorAssetSourceTokens.TextElement)
            .Select(element => element.Value));
        foreach (var label in SiteVectorAssetTokens.RequiredLabels)
        {
            await Assert.That(text.Contains(label, StringComparison.OrdinalIgnoreCase)).IsTrue();
        }
    }

    private static async Task AssertCanonicalKeyPath(XDocument poster, string repository)
    {
        var favicon = XDocument.Load(Path.Combine(repository, SiteVectorAssetTokens.FaviconRelativePath), LoadOptions.None);
        var canonicalPath = favicon.Descendants().Single(element =>
            element.Name.LocalName == SiteVectorAssetSourceTokens.PathElement);
        var matchingPaths = poster.Descendants().Where(element =>
            element.Name.LocalName == SiteVectorAssetSourceTokens.PathElement &&
            (string?)element.Attribute(SiteVectorAssetSourceTokens.PathDataAttribute) ==
            (string?)canonicalPath.Attribute(SiteVectorAssetSourceTokens.PathDataAttribute)).ToArray();
        await Assert.That(matchingPaths.Length).IsEqualTo(SiteTokens.One);
        var path = matchingPaths.Single();
        foreach (var attributeName in SiteVectorAssetSourceTokens.CanonicalPathAttributes)
        {
            await Assert.That((string?)path.Attribute(attributeName)).IsEqualTo((string?)canonicalPath.Attribute(attributeName));
        }
    }

    private static async Task VerifyPoster(string posterPath, string repository)
    {
        XDocument poster;
        try
        {
            poster = XDocument.Load(posterPath, LoadOptions.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            throw new InvalidDataException(SiteVectorAssetTokens.MissingPosterFailure, exception);
        }

        await Assert.That(poster.Root?.Name.LocalName).IsEqualTo(SiteVectorAssetSourceTokens.SvgElement);
        await Assert.That(poster.Root?.Name.NamespaceName).IsEqualTo(SiteVectorAssetTokens.SvgNamespace);
        await Assert.That(HasVectorGeometry(poster)).IsTrue();
        await AssertSafeVector(poster);
        await AssertModelLabels(poster);
        await AssertCanonicalKeyPath(poster, repository);
    }
}

internal static class SiteVectorAssetHtml
{
    public static IReadOnlyList<SiteVectorImage> Images(string html)
    {
        return SiteVectorAssetSourceTokens.HtmlImageTag.Matches(html).Select(match => SiteVectorAssetSourceTokens.HtmlAttribute.Matches(match.Value)
            .ToDictionary(attribute => attribute.Groups[SiteVectorAssetSourceTokens.RegexNameGroup].Value,
                attribute => attribute.Groups[SiteVectorAssetSourceTokens.RegexValueGroup].Value, StringComparer.OrdinalIgnoreCase))
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
