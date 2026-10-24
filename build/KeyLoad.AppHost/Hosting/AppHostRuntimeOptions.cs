using KeyLoad.AppHost.TestInfrastructure;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Hosting;

internal sealed record AppHostRuntimeOptions(IOptions<TestExecutionOptions> TestExecution, IOptions<AppHostStartupOptions> Startup,
    IOptions<AppHostControlOptions> Control, IOptions<ClusterDeploymentOptions> Cluster,
    IOptions<CommandAdmissionLimits> CommandAdmission, IOptions<HttpAdmissionLimits> HttpAdmission);
