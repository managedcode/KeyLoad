namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteVectorAssetTokens
{
    public const string PosterRelativePath = "site/Features/BenchmarkComparisons/assets/cluster-poster.svg";
    public const string IndexRelativePath = "site/Features/BenchmarkComparisons/index.html";
    public const string FaviconRelativePath = "site/favicon.svg";
    public const string PosterSelector = ".cluster-poster";
    public const string LiveMarkSelector = "img.cluster-core";
    public const string LiveMarkClass = "cluster-core";
    public const string CanonicalFaviconUrl = "./favicon.svg";
    public const string SvgNamespace = "http://www.w3.org/2000/svg";
    public const string HrefAttribute = "href";
    public const string XlinkHrefAttribute = "xlink:href";
    public const string DataScheme = "data:";
    public const string SvgMimeMarker = "image/svg+xml";
    public const string RasterMimeMarker = "image/";
    public const string ExternalScheme = "://";
    public const string ImageElement = "image";
    public const string ForeignObjectElement = "foreignObject";
    public const string ScriptElement = "script";
    public const string FillAttribute = "fill";
    public const string StrokeAttribute = "stroke";
    public const string SceneFallbackDescription = "Conceptual illustration, not live data.";
    public const string MissingPosterFailure = "The committed scene poster must be a readable SVG document.";
    public const string UnsafePosterFailure = "The scene poster must contain vector artwork without scripts or external assets.";
    public const string MissingModelLabelFailure = "The scene poster must name all eight data models.";
    public const string MissingLiveMarkFailure = "The page must contain one live cluster logo using the canonical favicon.";
    public const string MissingCanonicalFaviconFailure = "The canonical favicon source must exist as an SVG asset.";
    public const string LiveMarkNotReadyFailure = "The ready scene must expose exactly one loaded, projected canonical SVG mark.";
    public const string PosterFallbackFailure = "The poster state must show the vector illustration and hide the live mark.";
    public const string PosterLabelSql = "SQL";
    public const string PosterLabelDocuments = "Documents";
    public const string PosterLabelGraphs = "Graphs";
    public const string PosterLabelEvents = "Events";
    public const string PosterLabelVectors = "Vectors";
    public const string PosterLabelQueues = "Queues";
    public const string PosterLabelTimeSeries = "Time series";
    public const string PosterLabelBlobs = "Blobs";
    public const string DataImageScript = "(() => { const img = document.querySelectorAll('img.cluster-core'); const node = img[0]; const style = node ? getComputedStyle(node) : null; const rect = node ? node.getBoundingClientRect() : null; const poster = document.querySelector('.cluster-poster'); const posterStyle = poster ? getComputedStyle(poster) : null; return { count: img.length, loaded: !!node && node.complete && node.naturalWidth > 0 && node.naturalHeight > 0, source: node ? new URL(node.currentSrc || node.src, document.baseURI).pathname : '', display: style?.display || '', visibility: style?.visibility || '', opacity: style?.opacity || '', width: style?.width || '', height: style?.height || '', offsetWidth: node?.offsetWidth || 0, offsetHeight: node?.offsetHeight || 0, rectWidth: rect?.width || 0, rectHeight: rect?.height || 0, transform: style?.transform || '', posterVisibility: posterStyle?.visibility || '', posterDisplay: posterStyle?.display || '' }; })()";
    public const string PosterStateScript = "(() => { const poster = document.querySelector('.cluster-poster'); const mark = document.querySelector('img.cluster-core'); const p = poster ? getComputedStyle(poster) : null; const m = mark ? getComputedStyle(mark) : null; return { posterVisible: !!poster && p.display !== 'none' && p.visibility !== 'hidden' && Number(p.opacity) > 0, markVisible: !!mark && m.display !== 'none' && m.visibility !== 'hidden' && Number(m.opacity) > 0, markCount: document.querySelectorAll('img.cluster-core').length }; })()";
    public const string Matrix3dPrefix = "matrix3d(";
    public const string CountField = "count";
    public const string LoadedField = "loaded";
    public const string SourceField = "source";
    public const string DisplayField = "display";
    public const string VisibilityField = "visibility";
    public const string OpacityField = "opacity";
    public const string PosterVisibilityField = "posterVisibility";
    public const string TransformField = "transform";
    public const string PosterVisibleField = "posterVisible";
    public const string MarkVisibleField = "markVisible";
    public const string MarkCountField = "markCount";
    public const string WidthField = "width";
    public const string HeightField = "height";
    public const string OffsetWidthField = "offsetWidth";
    public const string OffsetHeightField = "offsetHeight";
    public const string RectWidthField = "rectWidth";
    public const string RectHeightField = "rectHeight";
    public const string PosterExpectedDisplay = "block";
    public const string PosterExpectedVisibility = "visible";
    public const string HiddenVisibility = "hidden";
    public const string CssNone = "none";
    public const string RepositoryRootFile = "site/scripts/build.mjs";
    public const string HtmlImageTagPrefix = "<img";
    public const string ClassAttributeName = "class";
    public const string SourceAttributeName = "src";
    public const string Quote = "\"";
    public const string TagEnd = ">";
    public const string Whitespace = " ";
    public const string CssPixelSuffix = "px";
    public const string ExpectedFaviconPath = "/favicon.svg";
    public const string RegexNameGroup = "name";
    public const string RegexValueGroup = "value";
    public const char MatrixClosingCharacter = ')';
    public const double PositiveSize = 0;
    public const double CssDimensionTolerance = 1;

    public static readonly string[] RequiredLabels =
    [
        PosterLabelSql,
        PosterLabelDocuments,
        PosterLabelGraphs,
        PosterLabelEvents,
        PosterLabelVectors,
        PosterLabelQueues,
        PosterLabelTimeSeries,
        PosterLabelBlobs,
    ];
}
