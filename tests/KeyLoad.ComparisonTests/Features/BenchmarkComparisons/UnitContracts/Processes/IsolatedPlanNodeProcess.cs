using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed record IsolatedPlanProcessResult(int ExitCode, string Output, string Error);

internal static class IsolatedPlanNodeProcess
{
    private const string Failure = "The isolated plan Node process failed.";
    private const int OutputLimit = 512 * 1024;
    private const string Probe = """
        import { readFileSync } from 'node:fs';
        import { pathToFileURL } from 'node:url';
        const plannerUrl = pathToFileURL(process.env.KEYLOAD_ISOLATED_PLAN_MODULE);
        const planner = await import(plannerUrl.href);
        const preflight = await import(new URL('./isolated-preflight.mjs', plannerUrl).href);
        const scale = await import(new URL('./scaled-isolated-plan.mjs', plannerUrl).href);
        const vectors = await import(new URL('./vector-isolated-plan.mjs', plannerUrl).href);
        const openLoop = await import(new URL('./open-loop-isolated-plan.mjs', plannerUrl).href);
        const request = JSON.parse(readFileSync(0, 'utf8'));
        function workflowMatrices() {
          const matrices = preflight.createDatabaseMatrices(request.plan, request.scaledPlans,
            request.vectorPlans, request.openLoopPlan);
          return Object.fromEntries(Object.entries(matrices).map(([key, matrix]) => [key, { include: matrix.include.map(row => ({
            id: row.id, jobName: row.jobName, target: row.target,
            kind: row.preflight ? 'preflight' : row.openLoopCancellationProof ? 'proof'
              : row.openLoopRate === undefined ? 'worker' : 'open-loop'
          })) }]));
        }
        try {
          const contract = request.contract ?? await planner.readIsolatedContract();
          const value = request.operation === 'create' ? planner.createIsolatedPlan(contract)
            : request.operation === 'create-scales' ? scale.createScaledPlans(contract)
            : request.operation === 'create-vectors' ? vectors.createVectorPlans(contract)
            : request.operation === 'create-composite' ? scale.createCompositePlan(planner.createIsolatedPlan(contract), scale.createScaledPlans(contract), vectors.createVectorPlans(contract))
            : request.operation === 'validate-scales' ? scale.validateScaledPlans(request.plan, contract)
            : request.operation === 'create-open-loop' ? openLoop.createOpenLoopPlan()
            : request.operation === 'workflow-matrices' ? workflowMatrices()
            : request.operation === 'matrices' ? preflight.createDatabaseMatrices(request.plan, request.scaledPlans, request.vectorPlans, request.openLoopPlan)
            : request.operation === 'preflight' ? preflight.createPreflightMatrix(request.plan)
            : planner.validateIsolatedPlan(request.plan, contract);
          process.stdout.write(JSON.stringify({ ok: true, value }));
        } catch {
          process.stdout.write(JSON.stringify({ ok: false }));
        }
        """;

    internal static async Task<JsonObject> ProbeAsync(string operation, JsonNode? plan = null,
        JsonNode? contract = null, JsonNode? scaledPlans = null, JsonNode? vectorPlans = null,
        JsonNode? openLoopPlan = null)
    {
        var request = new JsonObject
        {
            [IsolatedPlanFields.Operation] = operation,
            [IsolatedPlanFields.Plan] = plan?.DeepClone(),
            [IsolatedPlanFields.Contract] = contract?.DeepClone(),
            [IsolatedPlanFields.ScalePlan] = scaledPlans?.DeepClone(),
            [IsolatedPlanFields.VectorPlansInput] = vectorPlans?.DeepClone(),
            [IsolatedPlanFields.OpenLoopPlan] = openLoopPlan?.DeepClone()
        };
        var result = await RunAsync(["--input-type=module", "-e", Probe], request.ToJsonString());
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Error).IsEmpty();
        return JsonNode.Parse(result.Output)?.AsObject() ?? throw new InvalidOperationException(Failure);
    }

    internal static Task<IsolatedPlanProcessResult> CliAsync(params string[] arguments)
        => RunAsync([ModulePath, .. arguments], null);

    internal static Task<IsolatedPlanProcessResult> MatrixEntryAsync(string planDirectory, string githubEnvPath,
        string target, string id, string jobName, string kind)
        => RunAsync([MatrixEntryModulePath], null, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IsolatedPlanFields.MatrixPlanDirectoryEnvironment] = planDirectory,
            [IsolatedPlanFields.GithubEnvironment] = githubEnvPath,
            [IsolatedPlanFields.MatrixTargetEnvironment] = target,
            [IsolatedPlanFields.MatrixIdEnvironment] = id,
            [IsolatedPlanFields.JobNameEnvironment] = jobName,
            [IsolatedPlanFields.MatrixKindEnvironment] = kind
        });

    internal static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "KeyLoad.slnx")))
                { return directory.FullName; }
            }
            throw new DirectoryNotFoundException(Failure);
        }
    }

    internal static string ContractPath => Path.Combine(RepositoryRoot, "benchmarks", "KeyLoad.Comparisons",
        "Features", "BenchmarkComparisons", "isolated-contract.json");

    private static string ModulePath => Path.Combine(RepositoryRoot, "scripts", "Features",
        "BenchmarkComparisons", "isolated-plan.mjs");

    private static string MatrixEntryModulePath => Path.Combine(RepositoryRoot, "scripts", "Features",
        "BenchmarkComparisons", "isolated-matrix-entry.mjs");

    private static async Task<IsolatedPlanProcessResult> RunAsync(string[] arguments, string? input,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        using var deadlineTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, deadlineTimeout.Token);
        using var process = new Process { StartInfo = CreateStart(arguments, environment) };
        if (!process.Start())
        { throw new InvalidOperationException(Failure); }
        var output = ReadBoundedAsync(process.StandardOutput, deadline.Token);
        var error = ReadBoundedAsync(process.StandardError, deadline.Token);
        try
        {
            if (input is not null)
            { await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token); }
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            var captured = await Task.WhenAll(output, error).WaitAsync(deadline.Token);
            return new(process.ExitCode, captured[0], captured[1]);
        }
        finally
        {
            await StopAndObserveAsync(process, deadline, output, error);
        }
    }

    private static ProcessStartInfo CreateStart(string[] arguments, IReadOnlyDictionary<string, string>? environment)
    {
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        { start.ArgumentList.Add(argument); }
        var executablePath = Environment.GetEnvironmentVariable(IsolatedPlanFields.PathEnvironment);
        start.Environment.Clear();
        if (executablePath is not null)
        { start.Environment[IsolatedPlanFields.PathEnvironment] = executablePath; }
        start.Environment[IsolatedPlanFields.ModuleEnvironment] = ModulePath;
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            { start.Environment[key] = value; }
        }
        return start;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0)
            { return result.ToString(); }
            if (result.Length + count > OutputLimit)
            { throw new InvalidOperationException(Failure); }
            result.Append(buffer, 0, count);
        }
    }

    private static async Task StopAndObserveAsync(Process process, CancellationTokenSource deadline,
        Task<string> output, Task<string> error)
    {
        try
        {
            if (!process.HasExited)
            { process.Kill(entireProcessTree: true); }
        }
        catch (InvalidOperationException) when (process.HasExited)
        { /* Natural exit may win the race with cleanup. */ }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3), TimeProvider.System);
        await process.WaitForExitAsync(cleanup.Token);
        await deadline.CancelAsync();
        try
        { await Task.WhenAll(output, error).WaitAsync(cleanup.Token); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        { /* Cancellation closes the owned bounded readers after process exit. */ }
    }
}
