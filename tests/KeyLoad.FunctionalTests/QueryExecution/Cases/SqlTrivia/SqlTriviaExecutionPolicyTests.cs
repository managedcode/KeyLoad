using System.Globalization;
using KeyLoad.Client;
using KeyLoad.FunctionalTests.ClientApi;
using KeyLoad.Query;
using KeyLoad.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.FunctionalTests.QueryExecution;

/// <summary>AC-CQ-034/035 and AC-SQLC-004/005: execution SQL work and conservative cancellation policy.</summary>
internal sealed class SqlTriviaExecutionPolicyTests
{
    private const string Trivia = " \r\n-- ignored ; CALL\r\n/* outer /* inner */ tail */";
    private const string SelectKeyword = "SELECT";
    private const string Select = "SELECT * FROM orders";
    private const string Explain = "/* xx */SELECT * FROM orders";
    private const string Unclosed = "/* unfinished";
    private const string SuccessReply = "{}";
    private const string ApiKey = "sql-inspection-policy-test-key";
    private const int OriginalInterval = 256;
    private const int OriginalInspectionBytes = 65_536;

    [Test]
    public async Task OmittedSettingsRetainTheIndependentOriginalDefault()
    {
        var configuration = Configuration(null);
        using var configurationLifetime = configuration as IDisposable;
        using var provider = Provider(configuration);
        provider.GetRequiredService<IStartupValidator>().Validate();
        await Assert.That(provider.GetRequiredService<IOptions<QueryExecutionOptions>>().Value.SqlBudgetCheckInterval)
            .IsEqualTo(OriginalInterval);
        await Assert.That(provider.GetRequiredService<IOptions<KeyLoadClientExecutionOptions>>().Value.MaximumSqlInspectionBytes)
            .IsEqualTo(OriginalInspectionBytes);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(OriginalInterval)]
    public async Task GrammarAtomsRetainPositiveBoundedProgressAndOriginalOffsets(int interval)
    {
        var nested = Parse(Trivia + Select, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(nested is global::Microsoft.SqlServer.TransactSql.ScriptDom.SelectStatement).IsTrue();
        await Assert.That(nested.ScriptTokenStream[nested.FirstTokenIndex].Offset).IsEqualTo(Trivia.Length);
        var boundary = new string(' ', interval - 1) + "/* nested /* x */ tail */";
        var edge = Parse(boundary + Select, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(edge.ScriptTokenStream[edge.FirstTokenIndex].Offset).IsEqualTo(boundary.Length);
        var unclosed = Assert.ThrowsExactly<KeyLoadException>(
            () => Parse(Unclosed, TestContext.Current!.Execution.CancellationToken));
        await Assert.That(unclosed.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(OriginalInterval)]
    public async Task ConfiguredQueryAndServerOwnersPreserveTokensAndCanonicalPayload(int interval)
    {
        var configuration = Configuration(interval);
        using var configurationLifetime = configuration as IDisposable;
        using var provider = Provider(configuration);
        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptions<QueryExecutionOptions>>();
        await Assert.That(options.Value.SqlBudgetCheckInterval).IsEqualTo(interval);
        var syntax = Parse(Trivia + Select, TestContext.Current!.Execution.CancellationToken);
        var tokens = syntax.ScriptTokenStream.Where(token => token.TokenType is not
            (global::Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.WhiteSpace
            or global::Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.SingleLineComment
            or global::Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.MultilineComment
            or global::Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.EndOfFile)).ToArray();
        string[] expected = [SelectKeyword, "*", "FROM", "orders"];
        await Assert.That(tokens.Select(token => token.Text).SequenceEqual(expected)).IsTrue();
        await Assert.That(tokens[0].Offset).IsEqualTo(Trivia.Length);
        var request = SqlOperationCommentTestData.Request(Trivia + SqlOperationCommentTestData.PlainCall);
        var operation = SqlOperationCompiler.Compile(request,
            SqlOperationTestData.MaximumPayloadBytes, options);
        await SqlOperationTestData.Same(operation, SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
    }

    [Test]
    [Arguments(1, Select, ErrorCode.UnknownWriteOutcome)]
    [Arguments(2, Select, ErrorCode.UnknownWriteOutcome)]
    [Arguments(3, Select, ErrorCode.UnknownWriteOutcome)]
    [Arguments(8, Select, ErrorCode.Cancelled)]
    [Arguments(8, Explain, ErrorCode.UnknownWriteOutcome)]
    [Arguments(16, Explain, ErrorCode.Cancelled)]
    [Arguments(8, "  SELECT * FROM orders", ErrorCode.UnknownWriteOutcome)]
    [Arguments(8, " SELECT * FROM orders", ErrorCode.Cancelled)]
    public async Task SdkUsesTheSameBoundPrefixAndKeywordBoundary(int interval, string sql, ErrorCode expected)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var configuration = Configuration(interval);
            using var configurationLifetime = configuration as IDisposable;
            using var provider = Provider(configuration);
            provider.GetRequiredService<IStartupValidator>().Validate();
            var options = provider.GetRequiredService<IOptions<KeyLoadClientExecutionOptions>>();
            await Assert.That(options.Value.MaximumSqlInspectionBytes).IsEqualTo(interval);
            var calls = 0;
            SqlOperationRequest? received = null;
            await using var server = await KeyLoadClientKestrelServer.StartAsync(async context =>
            {
                Interlocked.Increment(ref calls);
                received = await global::System.Text.Json.JsonSerializer.DeserializeAsync<SqlOperationRequest>(
                    context.Request.Body, JsonDefaults.Options, context.RequestAborted);
                await context.Response.WriteAsync(SuccessReply, context.RequestAborted);
            });
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var request = new SqlOperationRequest(McpCanonicalTestData.Partition, sql);
                var result = await new KeyLoadClient(server.Client, ApiKey, options).ExecuteSqlAsync(request, cancellation.Token);
                await Assert.That(result.IsFailed).IsTrue();
                await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
                await Assert.That(calls).IsEqualTo(0);
                var healthyRequest = SqlOperationCommentTestData.Request(Trivia + SqlOperationCommentTestData.PlainCall);
                var healthy = await new KeyLoadClient(server.Client, ApiKey, options)
                    .ExecuteSqlAsync(healthyRequest, TestContext.Current!.Execution.CancellationToken);
                await Assert.That(healthy.IsSuccess).IsTrue();
                await Assert.That(healthy.Value.GetRawText()).IsEqualTo(SuccessReply);
                await Assert.That(calls).IsEqualTo(1);
                await Assert.That(JsonDefaults.Serialize(received).SequenceEqual(JsonDefaults.Serialize(healthyRequest))).IsTrue();
                var operation = SqlOperationCompiler.Compile(healthyRequest,
                    SqlOperationTestData.MaximumPayloadBytes, provider.GetRequiredService<IOptions<QueryExecutionOptions>>());
                await SqlOperationTestData.Same(operation, SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    [Test]
    [Arguments(0)]
    [Arguments(65_537)]
    public async Task InvalidClientCadenceFailsStartupBeforeInspection(int interval)
    {
        var configuration = Configuration(interval, includeQuery: false);
        using var configurationLifetime = configuration as IDisposable;
        using var provider = Provider(configuration);
        var failure = Assert.ThrowsExactly<OptionsValidationException>(provider.GetRequiredService<IStartupValidator>().Validate);
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(KeyLoadClientExecutionOptions));
        await Assert.That(failure.Failures.Single()).IsEqualTo(KeyLoadClientExecutionOptions.ValidationMessage);
    }

    private static global::Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement Parse(
        string sql, CancellationToken token)
    {
        var budget = new global::KeyLoad.Core.ReadExecutionBudget(cancellationToken: token);
        return global::KeyLoad.QueryExecution.ScriptDomSyntax.Parse(sql, budget.Check, token);
    }

    private static IConfigurationRoot Configuration(int? interval, bool includeQuery = true)
    {
        var settings = new Dictionary<string, string?>();
        if (interval is { } value)
        {
            var text = value.ToString(CultureInfo.InvariantCulture);
            settings[$"{KeyLoadClientExecutionOptions.SectionName}:{nameof(KeyLoadClientExecutionOptions.MaximumSqlInspectionBytes)}"] = text;
            if (includeQuery)
            {
                settings[$"{QueryExecutionOptions.SectionName}:{nameof(QueryExecutionOptions.SqlBudgetCheckInterval)}"] = text;
            }
        }
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static ServiceProvider Provider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        CoreRuntimeOptionsRegistration.AddCoreRuntimeOptions(services, configuration);
        services.AddKeyLoadClientExecutionOptions(configuration);
        return services.BuildServiceProvider();
    }
}
