using System.Net.Http.Json;
using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class Neo4jHarnessMismatchObserver(
    Uri endpoint,
    string password,
    string runId,
    BenchmarkDataset dataset,
    CancellationToken runToken) : IAsyncDisposable
{
    private static TimeSpan MutationTimeout => NativeExecutionPolicyFixture.Harness().Value.Neo4jMutationTimeout;
    private const string RestoreFailureMessage = "Neo4jHarnessSeedRestoreMismatch";
    private const string HttpStatusPrefix = ":HTTP";
    private readonly HttpClient client = Neo4jHarnessQueryClient.CreateClient(endpoint, password);
    private readonly BenchmarkDocument[] seedDocuments = dataset.Documents.ToArray();
    private readonly string label = Neo4jHarnessConstants.DocumentLabelPrefix + Guid.Parse(runId).ToString("N");
    private bool restoreRequired;

    public bool CorruptedBeforePointRead { get; private set; }
    public bool RestoredBeforeDocumentWrite { get; private set; }

    public void Observe(string progress)
    {
        if (!CorruptedBeforePointRead && progress.StartsWith(Neo4jHarnessConstants.PointReadProgress, StringComparison.Ordinal))
        {
            restoreRequired = true;
            UpdateSeedJson(Neo4jHarnessConstants.MutatedJson, runToken);
            CorruptedBeforePointRead = true;
        }

        if (restoreRequired && !RestoredBeforeDocumentWrite
            && progress.StartsWith(Neo4jHarnessConstants.DocumentWriteProgress, StringComparison.Ordinal))
        {
            RestoreAndVerify(runToken);
            restoreRequired = false;
            RestoredBeforeDocumentWrite = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await Neo4jHarnessFailureCollector.AttemptBoundedAsync(token =>
        {
            RestoreAndVerify(token);
            return Task.CompletedTask;
        }, failures);
        await Neo4jHarnessFailureCollector.AttemptAsync(() =>
        {
            client.Dispose();
            return Task.CompletedTask;
        }, failures);
        Neo4jHarnessFailureCollector.ThrowIfAny(RestoreFailureMessage, failures);
    }

    private void RestoreAndVerify(CancellationToken cancellationToken)
    {
        UpdateSeedJson(seedDocuments[0].Json, cancellationToken, restoreAll: true);
        VerifyStoredSeeds(cancellationToken);
    }

    private void UpdateSeedJson(string value, CancellationToken cancellationToken, bool restoreAll = false)
    {
        using var timeout = new CancellationTokenSource(MutationTimeout);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var documents = seedDocuments.Select(document => new { document.Id, json = restoreAll ? document.Json : value }).ToArray();
        var statement = Neo4jHarnessStatements.UpdateDocuments(label);
        using var response = SendStatement(statement, new { documents }, bounded.Token);
    }

    private void VerifyStoredSeeds(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(MutationTimeout);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var statement = Neo4jHarnessStatements.ReadDocuments(label);
        using var document = SendStatement(statement, new { ids = seedDocuments.Select(item => item.Id).ToArray() }, bounded.Token);
        var rows = document.RootElement.GetProperty(Neo4jHarnessConstants.DataProperty)
            .GetProperty(Neo4jHarnessConstants.ValuesProperty);
        var stored = rows.EnumerateArray().ToDictionary(row => row[0].GetString()!, row => row[1].GetString()!, StringComparer.Ordinal);
        if (stored.Count != seedDocuments.Length || seedDocuments.Any(item => !stored.TryGetValue(item.Id, out var json) || json != item.Json))
        {
            throw new ComparisonFailureException(RestoreFailureMessage);
        }
    }

    private JsonDocument SendStatement(string statement, object parameters, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Neo4jHarnessConstants.QueryPath)
        {
            Content = JsonContent.Create(new { statement, parameters, maxExecutionTime = (int)MutationTimeout.TotalSeconds })
        };
        using var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        using var stream = response.Content.ReadAsStream(cancellationToken);
        var document = JsonDocument.Parse(stream);
        try
        {
            Neo4jQueryResponse.ValidateErrors(document.RootElement);
            if (response.StatusCode != System.Net.HttpStatusCode.Accepted)
            {
                throw new ComparisonFailureException(Neo4jHarnessConstants.InvalidQueryResponse + HttpStatusPrefix
                    + ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return document;
        }
        catch (ComparisonFailureException)
        {
            document.Dispose();
            throw;
        }
    }
}
