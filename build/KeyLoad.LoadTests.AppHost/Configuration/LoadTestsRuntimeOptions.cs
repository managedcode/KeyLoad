using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

internal sealed record LoadTestsRuntimeOptions(AppHostRuntimeOptions Execution, IOptions<LoadTestsStartupOptions> Startup,
    IOptions<ScaleServerResourceOptions> ServerResources, IOptions<BenchmarkProvenanceOptions> Provenance,
    IOptions<BenchmarkDeploymentOptions> Deployment, IOptions<BenchmarkRelayOptions> BenchmarkRelay);
