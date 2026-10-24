using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

internal static class AdditionalSqlReadiness
{
    private const string Connection = "ConnectionString";
    private const string NotReady = "Native database connection is not ready.";

    internal static void Bind(IsolatedResourceContext context, IResourceBuilder<ContainerResource> node,
        ReferenceExpression connection)
    {
        var name = node.Resource.Name;
        context.BindSetting(Connection, connection);
        var timeout = LoadTestsOptionsRegistration.Get(context.Builder).Execution.TestExecution.Value.ProcessSettlementTimeout;
        context.Builder.Services.AddHealthChecks().AddAsyncCheck(name, async token =>
        {
            var value = await connection.GetValueAsync(token).ConfigureAwait(false);
            return value is not null && await RelationalComparisonTargets.IsReadyAsync(context.Selection.Target,
                value, timeout, token).ConfigureAwait(false)
                ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(NotReady);
        });
        node.WithHealthCheck(name);
    }
}
