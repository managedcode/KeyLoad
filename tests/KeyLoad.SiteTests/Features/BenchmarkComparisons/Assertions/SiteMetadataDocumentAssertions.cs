using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteMetadataDocumentAssertions
{
    internal static async Task VerifyAsync(string html, string output, CancellationToken token)
    {
        var head = ReadHead(html);
        var meta = ReadMeta(head);
        var links = ReadLinks(head);
        var title = Regex.Matches(head, SiteMetadataTokens.TitlePattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        await Assert.That(title.Count).IsEqualTo(1);
        await Assert.That(WebUtility.HtmlDecode(title[0].Groups[SiteMetadataTokens.HtmlTextGroup].Value)).IsEqualTo(SiteMetadataTokens.IndexTitle);
        await Assert.That(meta[SiteMetadataTokens.DescriptionKey].Count).IsEqualTo(1);
        await AssertNonEmpty(meta[SiteMetadataTokens.DescriptionKey][0]);
        await Assert.That(meta[SiteMetadataTokens.RobotsKey].Count).IsEqualTo(1);
        await Assert.That(meta[SiteMetadataTokens.RobotsKey][0]).IsEqualTo(SiteMetadataTokens.IndexableRobots);
        await Assert.That(meta[SiteMetadataTokens.ThemeColorMetaKey].Count).IsEqualTo(1);
        await Assert.That(meta[SiteMetadataTokens.ThemeColorMetaKey][0]).IsEqualTo(SiteMetadataTokens.ThemeColor);
        await Assert.That(links[SiteMetadataTokens.CanonicalKey].Select(item => item[SiteMetadataTokens.HrefAttribute]))
            .Contains(SiteMetadataTokens.CanonicalUrl);
        await Assert.That(links[SiteMetadataTokens.CanonicalKey].Count).IsEqualTo(1);
        await AssertOgAndTwitter(meta);
        await AssertFaviconLinks(links);
        await Assert.That(html.Contains("<h1", StringComparison.OrdinalIgnoreCase)).IsTrue();
        await Assert.That(html.Contains("<nav", StringComparison.OrdinalIgnoreCase)).IsTrue();
        await Assert.That(Regex.IsMatch(html, SiteMetadataTokens.ForbiddenClaims,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).IsFalse();
        await AssertStructuredData(head);
        await AssertManifestAndCrawl(output, token);
    }

    private static string ReadHead(string html)
    {
        var match = Regex.Match(html, SiteMetadataTokens.HeadPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
        {
            throw new InvalidOperationException(SiteMetadataTokens.InvalidMetadata);
        }
        return match.Groups[1].Value;
    }

    private static Dictionary<string, List<string>> ReadMeta(string head)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(head, SiteMetadataTokens.MetaPattern, RegexOptions.IgnoreCase))
        {
            var attributes = ReadAttributes(match.Groups[SiteMetadataTokens.HtmlAttrsGroup].Value);
            var key = attributes.GetValueOrDefault(SiteMetadataTokens.PropertyAttribute)
                ?? attributes.GetValueOrDefault(SiteMetadataTokens.NameAttribute);
            if (key is null)
            {
                continue;
            }
            if (!values.TryGetValue(key, out var entries))
            {
                values.Add(key, entries = []);
            }
            entries.Add(WebUtility.HtmlDecode(attributes.GetValueOrDefault(SiteMetadataTokens.ContentAttribute) ?? string.Empty));
        }
        return values;
    }

    private static Dictionary<string, List<Dictionary<string, string>>> ReadLinks(string head)
    {
        var values = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(head, SiteMetadataTokens.LinkPattern, RegexOptions.IgnoreCase))
        {
            var attributes = ReadAttributes(match.Groups[SiteMetadataTokens.HtmlAttrsGroup].Value);
            if (!attributes.TryGetValue(SiteMetadataTokens.RelAttribute, out var rel))
            {
                continue;
            }
            foreach (var relation in rel.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!values.TryGetValue(relation, out var entries))
                {
                    values.Add(relation, entries = []);
                }
                entries.Add(attributes);
            }
        }
        return values;
    }

    private static Dictionary<string, string> ReadAttributes(string value)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(value, SiteMetadataTokens.AttributePattern))
        {
            var raw = match.Groups[SiteMetadataTokens.HtmlDoubleQuoteGroup].Success ? match.Groups[SiteMetadataTokens.HtmlDoubleQuoteGroup].Value : match.Groups[SiteMetadataTokens.HtmlSingleQuoteGroup].Value;
            attributes[match.Groups[SiteMetadataTokens.ManifestName].Value] = WebUtility.HtmlDecode(raw);
        }
        return attributes;
    }

    private static async Task AssertOgAndTwitter(Dictionary<string, List<string>> meta)
    {
        var required = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SiteMetadataTokens.OgTypeKey] = "website",
            [SiteMetadataTokens.OgSiteNameKey] = "KeyLoad",
            [SiteMetadataTokens.OgUrlKey] = SiteMetadataTokens.CanonicalUrl,
            [SiteMetadataTokens.OgLocaleKey] = "en_US",
            [SiteMetadataTokens.OgImageKey] = SiteMetadataTokens.SocialImageUrl,
            [SiteMetadataTokens.OgImageTypeKey] = "image/png",
            [SiteMetadataTokens.OgImageWidthKey] = "1200",
            [SiteMetadataTokens.OgImageHeightKey] = "630",
            [SiteMetadataTokens.OgImageAltKey] = "KeyLoad: a source-available database for AI agents",
            [SiteMetadataTokens.TwitterCardKey] = "summary_large_image",
            [SiteMetadataTokens.TwitterImageKey] = SiteMetadataTokens.SocialImageUrl,
            [SiteMetadataTokens.TwitterImageAltKey] = "KeyLoad: a source-available database for AI agents",
        };
        foreach (var (key, expected) in required)
        {
            await Assert.That(meta[key].Count).IsEqualTo(1);
            await Assert.That(meta[key][0]).IsEqualTo(expected);
        }
        foreach (var key in new[] { SiteMetadataTokens.OgTitleKey, SiteMetadataTokens.OgDescriptionKey, SiteMetadataTokens.TwitterTitleKey, SiteMetadataTokens.TwitterDescriptionKey })
        {
            await Assert.That(meta[key].Count).IsEqualTo(1);
            await AssertNonEmpty(meta[key][0]);
        }
        await Assert.That(meta[SiteMetadataTokens.OgTitleKey][0]).IsEqualTo(meta[SiteMetadataTokens.TwitterTitleKey][0]);
        await Assert.That(meta[SiteMetadataTokens.OgDescriptionKey][0]).IsEqualTo(meta[SiteMetadataTokens.TwitterDescriptionKey][0]);
        await Assert.That(meta[SiteMetadataTokens.DescriptionKey].Count).IsEqualTo(1);
        await Assert.That(meta.Keys.Count(key => key.StartsWith("og:", StringComparison.OrdinalIgnoreCase))).IsEqualTo(11);
    }

    private static async Task AssertFaviconLinks(Dictionary<string, List<Dictionary<string, string>>> links)
    {
        await AssertIconLink(links, SiteMetadataTokens.IconRel, "./favicon.svg", SiteMetadataTokens.IconTypeSvg, null);
        await AssertIconLink(links, SiteMetadataTokens.IconRel, "./favicon.ico", SiteMetadataTokens.BinaryMimeIco, "16x16 32x32 48x48");
        await AssertIconLink(links, SiteMetadataTokens.IconRel, "./favicon-32x32.png", SiteMetadataTokens.BinaryMimePng, "32x32");
        await AssertIconLink(links, SiteMetadataTokens.IconRel, "./favicon-96x96.png", SiteMetadataTokens.BinaryMimePng, "96x96");
        await AssertIconLink(links, SiteMetadataTokens.AppleTouchRel, "./apple-touch-icon.png", null, "180x180");
        await AssertIconLink(links, SiteMetadataTokens.ManifestRel, "./site.webmanifest", null, null);
    }

    private static async Task AssertIconLink(Dictionary<string, List<Dictionary<string, string>>> links,
        string rel, string href, string? type, string? sizes)
    {
        var matching = links[rel].Where(item => item.GetValueOrDefault(SiteMetadataTokens.HrefAttribute) == href).ToArray();
        await Assert.That(matching.Length).IsEqualTo(1);
        if (type is not null)
        {
            await Assert.That(matching[0].GetValueOrDefault(SiteMetadataTokens.TypeAttribute)).IsEqualTo(type);
        }
        if (sizes is not null)
        {
            await Assert.That(matching[0].GetValueOrDefault(SiteMetadataTokens.ManifestSizes)).IsEqualTo(sizes);
        }
    }

    private static async Task AssertStructuredData(string head)
    {
        var matches = Regex.Matches(head, SiteMetadataTokens.JsonLdPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        await Assert.That(matches.Count).IsEqualTo(1);
        using var document = JsonDocument.Parse(matches[0].Groups[SiteMetadataTokens.HtmlJsonGroup].Value);
        var root = document.RootElement;
        await Assert.That(root.GetProperty(SiteMetadataTokens.SchemaContextKey).GetString()).IsEqualTo(SiteMetadataTokens.ApplicationLd);
        var graph = root.GetProperty(SiteMetadataTokens.SchemaGraphKey).EnumerateArray().ToArray();
        await Assert.That(graph.Length).IsEqualTo(2);
        var webSite = graph.Single(item => item.GetProperty(SiteMetadataTokens.SchemaTypeKey).GetString() == SiteMetadataTokens.WebSiteSchemaType);
        var software = graph.Single(item => item.GetProperty(SiteMetadataTokens.SchemaTypeKey).GetString() == SiteMetadataTokens.SoftwareSchemaType);
        await Assert.That(webSite.GetProperty(SiteMetadataTokens.SchemaIdKey).GetString()).IsEqualTo(SiteMetadataTokens.WebSiteId);
        await Assert.That(software.GetProperty(SiteMetadataTokens.SchemaIdKey).GetString()).IsEqualTo(SiteMetadataTokens.SoftwareId);
        await Assert.That(webSite.GetProperty(SiteMetadataTokens.SchemaUrlKey).GetString()).IsEqualTo(SiteMetadataTokens.CanonicalUrl);
        await Assert.That(software.GetProperty(SiteMetadataTokens.SchemaUrlKey).GetString()).IsEqualTo(SiteMetadataTokens.CanonicalUrl);
        await Assert.That(software.GetProperty(SiteMetadataTokens.SchemaRepositoryKey).GetString()).IsEqualTo(SiteMetadataTokens.RepositoryUrl);
        await Assert.That(software.GetProperty(SiteMetadataTokens.SchemaLanguageKey).GetString()).IsEqualTo(SiteMetadataTokens.CSharp);
        await Assert.That(software.GetProperty(SiteMetadataTokens.SchemaLicenseKey).GetString()).IsEqualTo(SiteMetadataTokens.KeyLoadLicense);
        var json = Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(root));
        foreach (var forbidden in new[] { SiteMetadataTokens.SchemaOffers, SiteMetadataTokens.SchemaRating, SiteMetadataTokens.SchemaReview, SiteMetadataTokens.SchemaPotentialAction })
        {
            await Assert.That(json.Contains(forbidden, StringComparison.OrdinalIgnoreCase)).IsFalse();
        }
    }

    private static async Task AssertManifestAndCrawl(string output, CancellationToken token)
    {
        var robots = await File.ReadAllTextAsync(Path.Combine(output, SiteMetadataTokens.RobotsFile), token);
        await Assert.That(robots).Contains("Sitemap: " + SiteMetadataTokens.CanonicalUrl + "sitemap.xml");
        await Assert.That(robots).Contains(SiteMetadataTokens.RobotsAllowAll);
        await Assert.That(robots).Contains(SiteMetadataTokens.DisallowPrefix + " " + SiteMetadataTokens.AdminPath);
        await Assert.That(robots).DoesNotContain(SiteMetadataTokens.FaviconDisallow);
        await Assert.That(robots).DoesNotContain(SiteMetadataTokens.CardDisallow);
        var sitemap = await File.ReadAllTextAsync(Path.Combine(output, SiteMetadataTokens.SitemapFile), token);
        var document = XDocument.Parse(sitemap);
        XNamespace ns = SiteMetadataTokens.SitemapNamespace;
        var urls = document.Root!.Elements(ns + SiteMetadataTokens.SchemaUrlKey).ToArray();
        await Assert.That(urls.Length).IsEqualTo(1);
        await Assert.That(urls[0].Element(ns + "loc")?.Value).IsEqualTo(SiteMetadataTokens.CanonicalUrl);
        await Assert.That(urls.Any(item => item.Value.Contains(SiteMetadataTokens.AdminPath, StringComparison.Ordinal))).IsFalse();
    }

    private static async Task AssertNonEmpty(string value)
        => await Assert.That(string.IsNullOrWhiteSpace(value)).IsFalse();
}
