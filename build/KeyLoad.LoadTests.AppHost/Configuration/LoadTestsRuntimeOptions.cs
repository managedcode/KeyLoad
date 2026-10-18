using KeyLoad.AppHost.Hosting;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

internal sealed record LoadTestsRuntimeOptions(AppHostRuntimeOptions Native, IOptions<LoadTestsStartupOptions> Startup,
    IOptions<ScaleServerResourceOptions> ServerResources, IOptions<BenchmarkProvenanceOptions> Provenance,
    IOptions<BenchmarkDeploymentOptions> Deployment, IOptions<BenchmarkRelayOptions> BenchmarkRelay,
    IOptions<IsolatedKeyLoadAdmissionOptions> IsolatedAdmission);
