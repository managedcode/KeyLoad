using System.Text.RegularExpressions;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteVectorAssetSourceTokens
{
    public const string SvgElement = "svg";
    public const string PathElement = "path";
    public const string RectElement = "rect";
    public const string CircleElement = "circle";
    public const string PolygonElement = "polygon";
    public const string TextElement = "text";
    public const string ImageElement = "image";
    public const string ForeignObjectElement = "foreignObject";
    public const string ScriptElement = "script";
    public const string StyleElement = "style";
    public const string HrefAttribute = "href";
    public const string IdAttribute = "id";
    public const string PathDataAttribute = "d";
    public const string StrokeAttribute = "stroke";
    public const string StrokeWidthAttribute = "stroke-width";
    public const string StrokeLinecapAttribute = "stroke-linecap";
    public const string StrokeLinejoinAttribute = "stroke-linejoin";
    public const string FragmentPrefix = "#";
    public const string DataScheme = "data:";
    public const string ExternalScheme = "://";
    public const string ProtocolRelativePrefix = "//";
    public const string ImportRuleMarker = "@import";
    public const string UrlFunctionPattern = "url\\s*\\(";
    public const string SvgUrlReferencePattern = """url\(\s*#(?<fragment>[^)\s'";]+)\s*\)""";
    public const string SvgHrefFragmentPattern = "^#(?<fragment>[^#\\s]+)$";
    public const string FragmentGroup = "fragment";
    public const string TextJoinSeparator = " ";
    public const string QueryReferenceFailure = "The vector poster contains an unresolved or ambiguous local reference.";
    public const string MobilePosterRelativePath = "site/Features/BenchmarkComparisons/assets/cluster-poster-mobile.svg";

    public static readonly Regex SvgUrlReference = new(SvgUrlReferencePattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static readonly Regex SvgHrefFragment = new(SvgHrefFragmentPattern,
        RegexOptions.CultureInvariant);
    public static readonly Regex UrlFunction = new(UrlFunctionPattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static readonly string[] CanonicalPathAttributes =
    [
        PathDataAttribute,
        StrokeAttribute,
        StrokeWidthAttribute,
        StrokeLinecapAttribute,
        StrokeLinejoinAttribute,
    ];
    public static readonly string[] PosterPaths =
    [
        SiteVectorAssetTokens.PosterRelativePath,
        MobilePosterRelativePath,
    ];
}
