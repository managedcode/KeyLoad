using Microsoft.CodeAnalysis;

namespace KeyLoad.AnalyzerTests.CodeQuality;

internal sealed class OrleansConfigureAwaitTests
{
    private const string RuleId = OrleansConfigureAwaitAnalyzer.DiagnosticId;

    [Test]
    [Arguments("await task.ConfigureAwait(true);")]
    [Arguments("await task.ConfigureAwait(false);")]
    [Arguments("await task.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);")]
    [Arguments("await genericTask.ConfigureAwait(false);")]
    [Arguments("await valueTask.ConfigureAwait(false);")]
    [Arguments("await genericValueTask.ConfigureAwait(false);")]
    [Arguments("await foreach (var value in stream.ConfigureAwait(false)) { }")]
    [Arguments("await foreach (var value in stream.WithCancellation(token).ConfigureAwait(true)) { }")]
    [Arguments("await using (resource.ConfigureAwait(false)) { }")]
    [Arguments("Func<bool, ConfiguredTaskAwaitable> configure = task.ConfigureAwait;")]
    public async Task RejectsFrameworkAwaitConfigurationInAnInheritedGrain(string statement)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            using Orleans;
            public abstract class Base : Grain { }
            public class Reader : Base
            {
                public async Task Run(Task task, Task<int> genericTask, ValueTask valueTask, ValueTask<int> genericValueTask,
                    IAsyncEnumerable<int> stream, IAsyncDisposable resource, CancellationToken token)
                {
                    {{statement}}
                }
            }
            """;
        var diagnostics = await AnalyzerFixture.AnalyzeAsync(new OrleansConfigureAwaitAnalyzer(), source,
            AnalyzerFixture.ServerAssembly);
        await AnalyzerFixture.AssertFindingAsync(diagnostics, RuleId, DiagnosticSeverity.Error, source, statement);
    }

    [Test]
    [Arguments(AnalyzerFixture.ServerAssembly,
        "Reader(GrainId id, Silo silo, ILoggerFactory loggers) : GrainService(id, silo, loggers)", "")]
    [Arguments(AnalyzerFixture.ServerAssembly, "Reader : IGrainBase",
        "public abstract IGrainContext GrainContext { get; }")]
    [Arguments(AnalyzerFixture.ClusteringAssembly, "Reader", "")]
    public async Task RejectsConfigurationInServiceInterfaceAndClusteringCode(string assembly, string declaration, string member)
    {
        const string marker = "task.ConfigureAwait(false)";
        var source = $$"""
            using System.Threading.Tasks;
            using Microsoft.Extensions.Logging;
            using Orleans;
            using Orleans.Runtime;
            public abstract class {{declaration}}
            {
                {{member}}
                public async Task Run(Task task)
                {
                    async Task Read() => await {{marker}};
                    await Read();
                }
            }
            """;
        var diagnostics = await AnalyzerFixture.AnalyzeAsync(new OrleansConfigureAwaitAnalyzer(), source, assembly);
        await AnalyzerFixture.AssertFindingAsync(diagnostics, RuleId, DiagnosticSeverity.Error, source, marker);
    }

    [Test]
    public async Task AllowsOrdinaryAwaitAndUnrelatedMethods()
    {
        const string source = """
            using System.Threading.Tasks;
            using Orleans;
            public class Reader : Grain
            {
                public async Task Run(Task task)
                {
                    await task;
                    await ConfigureAwait(false);
                }
                private Task ConfigureAwait(bool value) => Task.CompletedTask;
            }
            public class Client
            {
                public async Task Run(Task task) => await task.ConfigureAwait(false);
            }
            """;
        var diagnostics = await AnalyzerFixture.AnalyzeAsync(new OrleansConfigureAwaitAnalyzer(), source,
            AnalyzerFixture.ServerAssembly);
        await Assert.That(diagnostics).IsEmpty();
    }
}
