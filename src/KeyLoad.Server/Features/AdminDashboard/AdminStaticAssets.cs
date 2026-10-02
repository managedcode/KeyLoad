using System.Collections.Frozen;

namespace KeyLoad.Server;

/// <summary>Finite embedded console shell; public assets never exempt data routes from authentication.</summary>
internal static class AdminStaticAssets
{
    private const string EntryPath = "/admin";
    private const string RootPath = "/admin/";
    private const string ResourcePrefix = "KeyLoad.AdminDashboard.";
    private const string Html = "text/html; charset=utf-8";
    private const string Css = "text/css; charset=utf-8";
    private const string JavaScript = "text/javascript; charset=utf-8";
    private const string Svg = "image/svg+xml";
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
    private static readonly string[] Stylesheets = ["brand.css", "layout.css", "views.css"];
    private static readonly string[] Scripts = ["constants.js", "text.js", "format.js", "dom.js", "tooltip.js", "charts.js",
        "metrics.js", "navigation.js", "browsing.js", "catalog.js", "errors.js", "overview.js", "performance.js",
        "nodes.js", "storage.js", "app.js"];
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
