using System.Globalization;
using KeyLoad.Client;
using KeyLoad.Features.QueryExecution;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>AC-CQ-034/035 and AC-SQLC-004/005: native SQL work and conservative cancellation policy.</summary>
internal sealed class SqlTriviaExecutionPolicyTests
{
    private const string Trivia = " \r\n-- ignored ; CALL\r\n/* outer /* inner */ tail */";
    private const string SelectKeyword = "SELECT";
    private const string Select = "SELECT * FROM orders";
    private const string Explain = "EXPLAIN SELECT * FROM orders";
    private const string Unclosed = "/* unfinished";
    private const string Deep = "/* /* */ */SELECT";
    private const string SuccessReply = "{}";
    private const string ApiKey = "sql-inspection-policy-test-key";
    private const int OriginalInterval = 256;
    private const int PairWidth = 2;
    private const int Depth = 2;

    [Test]
    public async Task OmittedNativeSettingsRetainTheIndependentOriginalDefault()
    {
        var configuration = Configuration(null);
        using var configurationLifetime = configuration as IDisposable;
        using var provider = Provider(configuration);
        provider.GetRequiredService<IStartupValidator>().Validate();
        await Assert.That(provider.GetRequiredService<IOptions<QueryExecutionOptions>>().Value.SqlBudgetCheckInterval)
            .IsEqualTo(OriginalInterval);
        await Assert.That(provider.GetRequiredService<IOptions<KeyLoadClientExecutionOptions>>().Value.SqlBudgetCheckInterval)
            .IsEqualTo(OriginalInterval);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(OriginalInterval)]
    public async Task GrammarAtomsRetainPositiveBoundedProgressAndOriginalOffsets(int interval)
    {
        var nested = await DrainAsync(Trivia + SelectKeyword, Depth, interval);
        await Assert.That(nested.Status).IsEqualTo(SqlTriviaStatus.Complete);
        await Assert.That(nested.Offset).IsEqualTo(Trivia.Length);
        var boundary = new string(' ', interval - 1) + "/* nested /* x */ tail */";
        var edge = await DrainAsync(boundary + SelectKeyword, Depth, interval);
        await Assert.That(edge.Status).IsEqualTo(SqlTriviaStatus.Complete);
        await Assert.That(edge.Offset).IsEqualTo(boundary.Length);
        var unclosed = await DrainAsync(Unclosed, Depth, interval);
        await Assert.That(unclosed.Status).IsEqualTo(SqlTriviaStatus.UnterminatedComment);
        await Assert.That(unclosed.Offset).IsEqualTo(Unclosed.Length);
        await Assert.That((await DrainAsync(Deep, 1, interval)).Status).IsEqualTo(SqlTriviaStatus.DepthLimitExceeded);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(OriginalInterval)]
    public async Task NativeConfiguredQueryAndServerOwnersPreserveTokensAndCanonicalPayload(int interval)
    {
        var configuration = Configuration(interval);
        using var configurationLifetime = configuration as IDisposable;
        using var provider = Provider(configuration);
        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptions<QueryExecutionOptions>>();
        await Assert.That(options.Value.SqlBudgetCheckInterval).IsEqualTo(interval);
        var tokens = SqlTokenizer.Lex(Trivia + Select, 16, Depth, options.Value.SqlBudgetCheckInterval);
        SqlToken[] expected = [new(SqlTokenKind.Identifier, SelectKeyword), new(SqlTokenKind.Symbol, "*"),
            new(SqlTokenKind.Identifier, "FROM"), new(SqlTokenKind.Identifier, "orders"), new(SqlTokenKind.End, string.Empty)];
        await Assert.That(tokens.SequenceEqual(expected)).IsTrue();
        var request = SqlOperationCommentTestData.Request(Trivia + SqlOperationCommentTestData.PlainCall);
        var operation = SqlOperationCompiler.Compile(request, UnitExecutionOptions.DatabaseLimits(),
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
    public async Task NativeSdkUsesTheSameBoundPrefixAndKeywordBoundary(int interval, string sql, ErrorCode expected)
    {
        var configuration = Configuration(interval);
        using var configurationLifetime = configuration as IDisposable;
        using var provider = Provider(configuration);
        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptions<KeyLoadClientExecutionOptions>>();
        await Assert.That(options.Value.SqlBudgetCheckInterval).IsEqualTo(interval);
        var calls = 0;
        await using var server = await KeyLoadClientKestrelServer.StartAsync(context =>
        {
            Interlocked.Increment(ref calls);
            return context.Response.WriteAsync(SuccessReply, context.RequestAborted);
        });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var request = new SqlOperationRequest(McpCanonicalTestData.Partition, sql);
        var result = await new KeyLoadClient(server.Client, ApiKey, options).ExecuteSqlAsync(request, cancellation.Token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(257)]
    public async Task InvalidNativeClientCadenceFailsStartupBeforeInspection(int interval)
    {
        var configuration = Configuration(interval, includeQuery: false);
        using var configurationLifetime = configuration as IDisposable;
        using var provider = Provider(configuration);
        var failure = Assert.ThrowsExactly<OptionsValidationException>(provider.GetRequiredService<IStartupValidator>().Validate);
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(KeyLoadClientExecutionOptions));
        await Assert.That(failure.Failures.Single()).IsEqualTo(KeyLoadClientExecutionOptions.ValidationMessage);
    }

    private static async Task<(SqlTriviaStatus Status, int Offset)> DrainAsync(string sql, int depth, int interval)
    {
        var state = new SqlTriviaState();
        var offset = 0;
        SqlTriviaStatus status;
        do
        {
            var before = offset;
            status = SqlTriviaReader.Read(sql, ref offset, ref state, depth, interval);
            await Assert.That(offset - before).IsLessThanOrEqualTo(Math.Max(interval, PairWidth));
            if (status == SqlTriviaStatus.More)
            { await Assert.That(offset).IsGreaterThan(before); }
        } while (status == SqlTriviaStatus.More);
        return (status, offset);
    }

    private static IConfigurationRoot Configuration(int? interval, bool includeQuery = true)
    {
        var settings = new Dictionary<string, string?>();
        if (interval is { } value)
        {
            var text = value.ToString(CultureInfo.InvariantCulture);
            settings[$"{KeyLoadClientExecutionOptions.SectionName}:{nameof(KeyLoadClientExecutionOptions.SqlBudgetCheckInterval)}"] = text;
            if (includeQuery)
            { settings[$"{QueryExecutionOptions.SectionName}:{nameof(QueryExecutionOptions.SqlBudgetCheckInterval)}"] = text; }
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
