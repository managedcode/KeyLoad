namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserSceneTokens
{
    public const string BrowserSceneUnsupported = "The browser did not reach a supported or explicitly unsupported scene state.";
    public const string BrowserSceneError = "A real browser renderer initialization error occurred.";
    public const string SilosField = "silos";
    public const string GrainsField = "grains";
    public const string LinksField = "links";
    public const string DescriptionField = "description";
    public const string LabelsField = "labels";
    public const string TextField = "text";
    public const string VisibleField = "visible";
    public const string ContainedField = "contained";
    public const string LabelsHiddenField = "labelsHidden";
    public const string GraphReadyPredicate = "document.querySelector('#cluster-scene')?.dataset.sceneState === 'ready' && document.querySelectorAll('#cluster-scene [data-silo-label]').length === 3 && [...document.querySelectorAll('#cluster-scene [data-silo-label]')].every(label => {const style=getComputedStyle(label);return style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity)>0;})";
    public const string ClusterGraphScript = """
        (() => {
          const host = document.querySelector('#cluster-scene');
          const bounds = host.getBoundingClientRect();
          const labels = [...host.querySelectorAll('.silo-label[data-silo-label]')];
          const description = document.querySelector('.cluster-poster').alt;
          return {
            silos: Number(host.dataset.sceneSilos), grains: Number(host.dataset.sceneGrains),
            links: Number(host.dataset.sceneLinks), description,
            labels: labels.map(label => {
              const rect = label.getBoundingClientRect(), style = getComputedStyle(label);
              return { text: label.textContent.replace(/\s+/g, ' ').trim(),
                visible: style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity) > 0,
                contained: rect.width > 0 && rect.height > 0 && rect.left >= bounds.left && rect.right <= bounds.right &&
                  rect.top >= bounds.top && rect.bottom <= bounds.bottom };
            })
          };
        })()
        """;
    public static readonly string[] SiloNames = ["A", "B", "C"];
}
