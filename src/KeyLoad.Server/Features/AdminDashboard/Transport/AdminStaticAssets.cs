using System.Collections.Frozen;

namespace KeyLoad.Server;

/// <summary>Finite embedded console shell; public assets never exempt data routes from authentication.</summary>
internal static class AdminStaticAssets
{
    private const string BrandStylesheet = "brand.css";
    private const string LayoutStylesheet = "layout.css";
    private const string ConstantsScript = "constants.js";
    private const string TextScript = "text.js";
    private const string Favicon = "favicon.ico";
    private const string SmallFavicon = "favicon-32x32.png";

    private const string ViewsStylesheet = "views.css";
    private const string FormattingScript = "format.js";
    private const string DomScript = "dom.js";
    private const string TooltipScript = "tooltip.js";
    private const string ChartsScript = "charts.js";
    private const string MetricsScript = "metrics.js";
    private const string NavigationScript = "navigation.js";
    private const string BrowsingScript = "browsing.js";
    private const string CatalogScript = "catalog.js";
    private const string ErrorsScript = "errors.js";
    private const string OverviewScript = "overview.js";
    private const string PerformanceScript = "performance.js";
    private const string NodesScript = "nodes.js";
    private const string StorageScript = "storage.js";
    private const string ApplicationScript = "app.js";
    private const string LargeFavicon = "favicon-96x96.png";
    private const string AppleTouchIcon = "apple-touch-icon.png";
    private const string SmallApplicationIcon = "icon-192.png";
    private const string LargeApplicationIcon = "icon-512.png";

    private const string EntryPath = "/admin";
    private const string RootPath = "/admin/";
    private const string ResourcePrefix = "KeyLoad.AdminDashboard.";
    private const string Html = "text/html; charset=utf-8";
    private const string Css = "text/css; charset=utf-8";
    private const string JavaScript = "text/javascript; charset=utf-8";
    private const string Svg = "image/svg+xml";
    private const string Png = "image/png";
    private const string Ico = "image/x-icon";
    private const string Logo = "logo.svg";
    private const string Index = "index.html";
    private const string NoStore = "no-store";
    private const string NoSniff = "nosniff";
    private const string NoReferrer = "no-referrer";
    private const string ContentPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; "
        + "img-src 'self' data:; base-uri 'none'; frame-ancestors 'none'; form-action 'self'; object-src 'none'";
    private const string CspHeader = "Content-Security-Policy";
    private const string ReferrerHeader = "Referrer-Policy";
    private static readonly string[] Methods = [HttpMethods.Get, HttpMethods.Head];
    private static readonly string[] Stylesheets = [BrandStylesheet, LayoutStylesheet, ViewsStylesheet];
    private static readonly string[] Scripts = [ConstantsScript, TextScript, FormattingScript, DomScript, TooltipScript, ChartsScript,
        MetricsScript, NavigationScript, BrowsingScript, CatalogScript, ErrorsScript, OverviewScript, PerformanceScript,
        NodesScript, StorageScript, ApplicationScript];
    private static readonly (string File, string Type)[] Icons =
    [
        (Favicon, Ico), (SmallFavicon, Png), (LargeFavicon, Png),
        (AppleTouchIcon, Png), (SmallApplicationIcon, Png), (LargeApplicationIcon, Png)
    ];
    private static readonly FrozenDictionary<string, (string File, string Type)> Assets = Catalog()
        .ToFrozenDictionary(StringComparer.Ordinal);

    private static Dictionary<string, (string, string)> Catalog()
    {
        var assets = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            [RootPath] = (Index, Html),
            [RootPath + Index] = (Index, Html),
            [RootPath + Logo] = (Logo, Svg)
        };
        foreach (var icon in Icons)
        { assets[RootPath + icon.File] = (icon.File, icon.Type); }
        foreach (var file in Stylesheets)
        { assets[RootPath + file] = (file, Css); }
        foreach (var file in Scripts)
        { assets[RootPath + file] = (file, JavaScript); }
        return assets;
    }

    internal static bool IsPublicRequest(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && (string.Equals(request.Path.Value, EntryPath, StringComparison.Ordinal)
            || Assets.ContainsKey(request.Path.Value ?? string.Empty));

    internal static void Map(WebApplication app)
    {
        app.MapMethods(EntryPath, Methods, (Func<HttpContext, IResult>)ServeEntry);
        foreach (var path in Assets.Keys)
        {
            if (string.Equals(path, RootPath, StringComparison.Ordinal))
            { continue; }
            app.MapMethods(path, Methods, (Func<HttpContext, IResult>)Serve);
        }
    }

    private static IResult ServeEntry(HttpContext context)
    {
        if (string.Equals(context.Request.Path.Value, EntryPath, StringComparison.Ordinal))
        { return Results.Redirect(RootPath); }
        return string.Equals(context.Request.Path.Value, RootPath, StringComparison.Ordinal)
            ? Serve(context) : Results.NotFound();
    }

    private static IResult Serve(HttpContext context)
    {
        if (!Assets.TryGetValue(context.Request.Path.Value ?? string.Empty, out var asset))
        { return Results.NotFound(); }
        var content = typeof(AdminStaticAssets).Assembly.GetManifestResourceStream(ResourcePrefix + asset.File);
        if (content is null)
        { return Results.NotFound(); }
        context.Response.Headers.CacheControl = NoStore;
        context.Response.Headers.XContentTypeOptions = NoSniff;
        context.Response.Headers[CspHeader] = ContentPolicy;
        context.Response.Headers[ReferrerHeader] = NoReferrer;
        return Results.Stream(content, asset.Type);
    }
}
