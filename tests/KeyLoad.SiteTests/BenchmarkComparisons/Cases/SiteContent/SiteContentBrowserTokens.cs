namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteContentBrowserTokens
{
    public const string SceneFailureDiagnosticScript = """
                                                       (() => {
                                                         const host = document.querySelector('#cluster-scene');
                                                         const box = host?.getBoundingClientRect();
                                                         return {state:host?.dataset.sceneState,frame:host?.dataset.frameState,
                                                           backend:host?.dataset.graphicsBackend,canvasCount:host?.querySelectorAll('canvas').length,
                                                           documentState:document.readyState,pageVisible:document.visibilityState,
                                                           scrollY:window.scrollY,viewportHeight:innerHeight,hostTop:box?.top,hostBottom:box?.bottom,
                                                           hash:location.hash,status:document.querySelector('[data-scene-status]')?.textContent.trim()};
                                                       })()
                                                       """;
    public const string ContentReadyPredicate = "document.readyState === 'complete' && document.querySelector('#benchmarks .empty-state') !== null";
    public const string Mobile = "mobile";
    public const string Empty = "empty";
    public const string Catalog = "catalog";
    public const string Rows = "rows";
    public const string Controls = "controls";
    public const string Scene = "scene";
    public const string Status = "status";
    public const string Poster = "poster";
    public const int MobileWidth = 390;
    public const int MobileHeight = 844;
    public const int DeviceScaleFactor = 1;
}
