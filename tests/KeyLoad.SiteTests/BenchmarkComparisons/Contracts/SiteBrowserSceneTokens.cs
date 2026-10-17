namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserSceneTokens
{
    public const string BrowserSceneUnsupported = "The browser did not reach a supported or explicitly unsupported scene state.";
    public const string BrowserSceneError = "A real browser renderer initialization error occurred.";
    public const string ModelsField = "models";
    public const string AgentsField = "agents";
    public const string ClientsField = "clients";
    public const string ModelLabelsField = "modelLabels";
    public const string LinksField = "links";
    public const string DescriptionField = "description";
    public const string CaptionsField = "captions";
    public const string MarksField = "marks";
    public const string LabelsHiddenField = "labelsHidden";
    public const int ModelCount = 8;
    public const int AgentCount = 1;
    public const int LinkCount = 8;
    public const string GraphReadyPredicate = "document.querySelector('#cluster-scene')?.dataset.sceneState === 'ready' && document.querySelector('#cluster-scene canvas') !== null";
    public const string ClusterGraphScript = """
        (() => {
          const host = document.querySelector('#cluster-scene');
          return {
            models: Number(host.dataset.sceneModels), agents: Number(host.dataset.sceneAgents),
            links: Number(host.dataset.sceneLinks), clients: Number(host.dataset.sceneClients),
            description: host.querySelector('.cluster-poster').alt,
            marks: host.querySelectorAll('img.cluster-core').length,
            captions: host.querySelectorAll('[data-silo-label], [data-graph-label], .client-label, .model-label').length,
            modelLabels: [...host.querySelectorAll('[data-model-name]')].map(label => ({
              text: label.textContent.replace(/\s+/g, ' ').trim(),
              accessible: !!label.closest('.sr-only') && label.getAttribute('aria-hidden') !== 'true'
            }))
          };
        })()
        """;
    public const string TextField = "text";
    public const string AccessibleField = "accessible";
    public static readonly string[] ModelNames =
        ["Documents", "Tables", "Graphs", "Vectors", "Queues", "Events", "Blobs", "Time series", "SQL"];
}
