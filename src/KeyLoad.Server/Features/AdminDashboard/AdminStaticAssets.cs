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
    private const string Index = "index.html";
    private const string NoStore = "no-store";
    private const string NoSniff = "nosniff";
    private const string NoReferrer = "no-referrer";
    private const string ContentPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; "
        + "img-src 'self' data:; base-uri 'none'; frame-ancestors 'none'; form-action 'self'; object-src 'none'";
    private const string CspHeader = "Content-Security-Policy";
    private const string ReferrerHeader = "Referrer-Policy";
    private static readonly string[] Methods = [HttpMethods.Get, HttpMethods.Head];
    private static readonly FrozenDictionary<string, (string File, string Type)> Assets =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            [RootPath] = (Index, Html),
            [RootPath + Index] = (Index, Html),
            [RootPath + "styles.css"] = ("styles.css", Css),
            [RootPath + "constants.js"] = ("constants.js", JavaScript),
            [RootPath + "dom.js"] = ("dom.js", JavaScript),
            [RootPath + "metrics.js"] = ("metrics.js", JavaScript),
            [RootPath + "rendering.js"] = ("rendering.js", JavaScript),
            [RootPath + "browsing.js"] = ("browsing.js", JavaScript),
            [RootPath + "app.js"] = ("app.js", JavaScript)
        }.ToFrozenDictionary(StringComparer.Ordinal);

    internal static bool IsPublicRequest(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && (string.Equals(request.Path.Value, EntryPath, StringComparison.Ordinal)
            || Assets.ContainsKey(request.Path.Value ?? string.Empty));

    internal static void Map(WebApplication app)
    {
        app.MapMethods(EntryPath, Methods, () => Results.Redirect(RootPath));
        foreach (var path in Assets.Keys)
        {
            app.MapMethods(path, Methods, (Func<HttpContext, IResult>)Serve);
        }
    }

    private static IResult Serve(HttpContext context)
    {
        var asset = Assets[context.Request.Path.Value!];
        var content = typeof(AdminStaticAssets).Assembly.GetManifestResourceStream(ResourcePrefix + asset.File);
        if (content is null) { return Results.NotFound(); }
        context.Response.Headers.CacheControl = NoStore;
        context.Response.Headers.XContentTypeOptions = NoSniff;
        context.Response.Headers[CspHeader] = ContentPolicy;
        context.Response.Headers[ReferrerHeader] = NoReferrer;
        return Results.Stream(content, asset.Type);
    }
}
