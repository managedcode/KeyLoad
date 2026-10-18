using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Hosting;

internal sealed record AppHostRuntimeOptions(IOptions<TestExecutionOptions> TestExecution, IOptions<AppHostStartupOptions> Startup,
    IOptions<ScaleServerResourceOptions> ServerResources, IOptions<BenchmarkProvenanceOptions> Provenance,
    IOptions<AppHostControlOptions> Control, IOptions<ContainerImageOptions> Images,
    IOptions<ContainerImageExecutionOptions> ImageExecution, IOptions<LocalImageOptions> LocalImage,
    IOptions<ClusterDeploymentOptions> Cluster, IOptions<CommandAdmissionLimits> CommandAdmission,
    IOptions<HttpAdmissionLimits> HttpAdmission, IOptions<KeyLoad.Clustering.ReplicaReplayLimits> ReplayAdmission, IOptions<ClusterProfileExecutionOptions> Profile, IOptions<BenchmarkDeploymentOptions> Deployment,
    IOptions<BenchmarkRelayOptions> BenchmarkRelay,
    IOptions<KeyLoad.Clustering.ReplicaReplayLimits> IsolatedReplayAdmission,
    IOptions<KeyLoad.Comparisons.IsolatedKeyLoadAdmissionOptions> IsolatedAdmission,
    IOptions<NativeCoverageExecutionOptions> NativeCoverage,
    IOptions<NativeCoverageClusterImageOptions> NativeCoverageClusterImage);
