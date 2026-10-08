using System.Net;
using Aspire.Hosting;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.IntegrationTests.Features.ClientApi;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Requires actual late data admission after all six native membership resources start.</summary>
internal static class TwoRf3MembershipDataReadiness
{
    private const string UnexpectedStatus = "An owned database readiness endpoint returned an unexpected status.";

    internal static async Task WaitAsync(DistributedApplication app, IOptions<TestExecutionOptions> execution,
        TimeProvider clock, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(clock);
        var options = execution.Value;
        if (!options.IsValid())
        {
            throw new OptionsValidationException(TestExecutionOptions.SectionName, typeof(TestExecutionOptions),
            [TestExecutionOptions.ValidationMessage]);
        }
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            using var http = McpCallerHttp.Create(app, node);
            await WaitForNodeAsync(http, options.DatabaseReadinessPollInterval, clock, token).ConfigureAwait(false);
        }
    }

    private static async Task WaitForNodeAsync(HttpClient http, TimeSpan interval, TimeProvider clock, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            using var response = await http.GetAsync(new Uri(TwoRf3MembershipProtocol.HealthReady, UriKind.Relative),
                HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK)
            { return; }
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
            { throw new InvalidOperationException(UnexpectedStatus); }
            await Task.Delay(interval, clock, token).ConfigureAwait(false);
        }
    }
}
