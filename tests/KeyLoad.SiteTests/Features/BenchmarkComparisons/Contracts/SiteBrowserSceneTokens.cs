namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserSceneTokens
{
    public const string BrowserSceneUnsupported = "The browser did not reach a supported or explicitly unsupported scene state.";
    public const string BrowserSceneError = "A real browser renderer initialization error occurred.";
    public const string SilosField = "silos";
    public const string GrainsField = "grains";
    public const string ClientsField = "clients";
    public const string ModelLabelsField = "modelLabels";
    public const string ClientLabelsField = "clientLabels";
    public const string LinksField = "links";
    public const string DescriptionField = "description";
    public const string LabelsField = "labels";
    public const string TextField = "text";
    public const string VisibleField = "visible";
    public const string ContainedField = "contained";
    public const string LabelsHiddenField = "labelsHidden";
    public const string GraphReadyPredicate = "document.querySelector('#cluster-scene')?.dataset.sceneState === 'ready' && document.querySelectorAll('#cluster-scene [data-silo-label]').length === 3 && document.querySelectorAll('#cluster-scene .model-label[data-graph-label]').length === 9 && document.querySelectorAll('#cluster-scene .client-label[data-graph-label]').length === 3 && [...document.querySelectorAll('#cluster-scene [data-silo-label], #cluster-scene [data-graph-label]')].every(label => {const style=getComputedStyle(label);return style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity)>0;})";
    public const string ClusterGraphScript = """
        (() => {
          const host = document.querySelector('#cluster-scene');
          const bounds = host.getBoundingClientRect();
          const readLabels = (selector, nameSelector) => [...host.querySelectorAll(selector)].map(label => {
            const rect = label.getBoundingClientRect(), style = getComputedStyle(label);
            return { text: (nameSelector ? label.querySelector(nameSelector).textContent : label.textContent).replace(/\s+/g, ' ').trim(),
              visible: style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity) > 0,
              contained: rect.width > 0 && rect.height > 0 && rect.left >= bounds.left && rect.right <= bounds.right &&
                rect.top >= bounds.top && rect.bottom <= bounds.bottom };
          });
          return {
            silos: Number(host.dataset.sceneSilos), grains: Number(host.dataset.sceneGrains),
            links: Number(host.dataset.sceneLinks), clients: Number(host.dataset.sceneClients),
            description: host.querySelector('.cluster-poster').alt,
            labels: readLabels('.silo-label[data-silo-label]'),
            modelLabels: readLabels('.model-label[data-graph-label]'),
            clientLabels: readLabels('.client-label[data-graph-label]', 'strong')
          };
        })()
        """;
    public static readonly string[] ModelNames =
        ["Documents", "Tables", "Graphs", "Vectors", "Queues", "Events", "Blobs", "Time series", "SQL"];
    public static readonly string[] ClientNames = ["Client 01", "Client 02", "Client 03"];
    public static readonly string[] SiloNames = ["A", "B", "C"];
}
