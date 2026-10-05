namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteContentTokens
{
    public const string NoneArgument = "--benchmarks=none";
    public const string EmptyState = "No verified benchmark results are available yet. Check back after a completed comparison run.";
    public const string EmptyClass = "empty-state";
    public const string IsolatedCatalogAttribute = "data-isolated-catalog";
    public const string IsolatedCatalogPath = "isolated-catalog.json";
    public const string AggregatePath = "aggregate.json";
    public const string ProjectionPath = "projection.json";
    public const string InstrumentClass = "instrument";
    public const string LoadingClass = "loading";
    public const string RetryClass = "retry";
    public const string FetchCall = "fetch(";
    public const string XmlHttpRequest = "XMLHttpRequest";
    public const string WebSocket = "new WebSocket";
    public const string NetworkRequestEvent = "Network.requestWillBeSent";
    public const string Event = "method";
    public const string Parameters = "params";
    public const string Request = "request";
    public const string Url = "url";
    public const string Width = "innerWidth";
    public const string MobilePosterSuffix = "cluster-poster-mobile.svg";
    public const string DesktopPosterSuffix = "cluster-poster.svg";
    public const string ProductTitle = "KeyLoad — the AI-native database for AI agents";
    public const string TitleField = "title";
    public const string StaticStatus = "Static illustration.";
    public const string BenchmarksSection = "benchmarks";
    public const string SceneElement = "cluster-scene";
    public const string SceneStatusSelector = "[data-scene-status]";
    public const string RootMetaCanonical = "<link rel=\"canonical\" href=\"https://www.keyload.cloud/\">";
    public const string DescriptionMeta = "<meta name=\"description\" content=\"Source-available AI-native database";
    public const string JsonLdType = "application/ld+json";
    public const string ContentMode = "none";
    public const string BenchmarkSource = "none";
    public const string Output = "output";
    public const string Mode = "mode";
    public const string SiteRevision = "siteRevision";
    public const string BenchmarkSourceProperty = "benchmarkSource";
    public const string MeasuredRevision = "measuredSourceRevision";
    public const string Cohort = "cohort";
    public const string Compression = "compression";
    public const string Vendor = "vendor";
    public const string Manifest = "manifest.json";
    public const string DataDirectory = "data";
    public const string WrongModeArgument = "--benchmarks=missing";
    public const string ConflictingMeasuredArgument = "--revision=0000000000000000000000000000000000000000";
    public const string UnsafeOutputName = "site-content-unsafe-output";
    public const string UniqueSuffix = "-";
    public const string CollisionOutputName = "site-content-collision-output";
    public const string SentinelFile = "sentinel.txt";
    public const string Sentinel = "preserve-existing-output";
    public const string OutputError = "Output must be a nonexistent isolated directory outside source and evidence inputs.";
    public const string InvalidArgumentError = "Use unique known --name=value arguments.";
    public const string ModeError = "Unknown benchmark mode.";
    public const string InvalidModeMessage = "Website qualification benchmark mode must be measured or none.";
    public const string BuilderModeMarker = "--benchmarks";
    public const string HtmlExtension = ".html";
    public const string JsonExtension = ".json";
    public const string ScriptExtension = ".mjs";
    public const string CssExtension = ".css";
    public const string Newline = "\n";
    public const string DesktopEmpty = "desktop";
    public const string MobileEmpty = "mobile";
    public const string LocalhostPrefix = "http://127.0.0.1:";
    public const string SourceFeaturePrefix = "site/Features/BenchmarkComparisons/";
    public const string OutputFeaturePrefix = "Features/BenchmarkComparisons/";

    public static readonly string[] ContentModules =
    [
        SiteAssetTokens.BootstrapModule,
        SiteAssetTokens.ContractsModule,
        SiteAssetTokens.SceneModule,
        SiteAssetTokens.GeometryModule,
        SiteAssetTokens.LifecycleModule,
        SiteAssetTokens.ObserversModule,
    ];

    public static readonly string[] StaticFeatureAssets =
    [
        SiteAssetTokens.Stylesheet,
        SiteAssetTokens.BrandStylesheet,
        SiteAssetTokens.TokenStylesheet,
        SiteAssetTokens.SceneStylesheet,
        SiteAssetTokens.PosterAsset,
        SiteAssetTokens.MobilePosterAsset,
    ];

    public static readonly string[] RootAssets =
    [
        SiteAssetTokens.FaviconSvg,
        "favicon.ico",
        "favicon-32x32.png",
        "favicon-96x96.png",
        "apple-touch-icon.png",
        "icon-192.png",
        "icon-512.png",
        "site.webmanifest",
        "og-image.png",
    ];
}
