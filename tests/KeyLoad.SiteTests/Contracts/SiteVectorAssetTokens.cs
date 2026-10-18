namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteVectorAssetTokens
{
    public const string PosterRelativePath = "site/Features/BenchmarkComparisons/assets/agent-context.png";
    public const string OriginalArtworkSha256 = "2351f55b813b575a1b2a782481aea747fed8d0c4268dbff442adc5fa138a537e";
    public const string ObsoleteDesktopPoster = "assets/cluster-poster.svg";
    public const string ObsoleteMobilePoster = "assets/cluster-poster-mobile.svg";
    public static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    public const string FaviconRelativePath = "site/favicon.svg";
    public const string SvgNamespace = "http://www.w3.org/2000/svg";
    public const string DataImageScript = """
        (() => {
          const host = document.querySelector('#cluster-scene'), canvas = host?.querySelector('canvas');
          const rect = canvas?.getBoundingClientRect(), bounds = host?.getBoundingClientRect();
          const poster = host?.querySelector('.cluster-poster');
          return { count: host?.querySelectorAll('canvas').length ?? 0,
            loaded: !!poster && poster.complete && poster.naturalWidth > 0 && poster.naturalHeight > 0,
            markCount: host?.querySelectorAll('img.cluster-core').length ?? 0,
            posterVisibility: poster ? getComputedStyle(poster).visibility : '',
            transform: host?.dataset.scenePose ?? '', width: canvas?.width ?? 0, height: canvas?.height ?? 0,
            contained: !!rect && !!bounds && rect.width > 0 && rect.height > 0 &&
              rect.left >= bounds.left && rect.right <= bounds.right && rect.top >= bounds.top && rect.bottom <= bounds.bottom,
            bufferPixels: Number(host?.dataset.bufferPixels ?? 0), drawCalls: Number(host?.dataset.drawCalls ?? 0),
            triangles: Number(host?.dataset.triangles ?? 0),
            pixelRatio: host && canvas ? Math.max(canvas.width / host.clientWidth, canvas.height / host.clientHeight) : 0 };
        })()
        """;
    public const string PosterStateScript = """
        (() => {
          const host = document.querySelector('#cluster-scene'), poster = host?.querySelector('.cluster-poster');
          const style = poster ? getComputedStyle(poster) : null;
          return { posterVisible: !!style && style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity) > 0,
            loaded: !!poster && poster.complete && poster.naturalWidth > 0 && poster.naturalHeight > 0,
            source: poster ? new URL(poster.currentSrc || poster.src, document.baseURI).pathname : '',
            markCount: host?.querySelectorAll('img.cluster-core').length ?? 0, description: poster?.alt ?? '',
            labelsHidden: host?.querySelectorAll('[data-silo-label], [data-graph-label], .client-label, .model-label').length === 0 };
        })()
        """;
    public const string AgentDescription = "agent";
    public const string DatabaseDescription = "database";
    public const string ConceptualDescription = "conceptual";
    public const string ContainedField = "contained";
    public const string ReadyPredicate = "document.querySelector('#cluster-scene')?.getAttribute('data-scene-state')==='ready'";
    public const string SceneErrorPredicate = "document.querySelector('#cluster-scene')?.getAttribute('data-scene-state')==='error'";
    public const string DisableCacheMethod = "Network.setCacheDisabled";
    public const string CacheDisabledField = "cacheDisabled";
    public const string NotFoundStatusMarker = "404";
    public const string CountField = "count";
    public const string LoadedField = "loaded";
    public const string SourceField = "source";
    public const string PosterVisibilityField = "posterVisibility";
    public const string TransformField = "transform";
    public const string PosterVisibleField = "posterVisible";
    public const string MarkCountField = "markCount";
    public const string WidthField = "width";
    public const string HeightField = "height";
    public const string HiddenVisibility = "hidden";
    public const int RetinaScale = 2;
}
