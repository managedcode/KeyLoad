using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedAggregateCliFixture : IDisposable
{
    private const string Prefix = "keyload-isolated-cli-";
    private const string PlanFile = "plan.json";
    private const string ProofFile = "proof.json";
    private const string InputName = "input";
    private const string OutputName = "output";
    private const string CliModule = "aggregate-cli.mjs";
    private const string PlanModule = "isolated-plan.mjs";
    private const string InputMode = "--input-type=module";
    private const string EvalMode = "-e";
    private const string PrepareSource = """
        import { writeFileSync } from 'node:fs';
        import { pathToFileURL } from 'node:url';
        const planner = await import(pathToFileURL(process.argv[3]).href);
        const plan = planner.createIsolatedPlan(planner.readIsolatedContract());
        const cohort = {sourceRevision:'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',runId:37070000000,attempt:1,
          repository:'managedcode/KeyLoad',ref:'refs/heads/main',workflow:'KeyLoad CI',profile:plan.profile};
        const cells = plan.cells.map((cell,index)=>({id:cell.id,
          job:{id:1000+index,name:'case / '+cell.id,url:'https://github.com/managedcode/KeyLoad/actions/runs/'+cohort.runId+'/job/'+(1000+index),
            conclusion:'success',steps:[{name:'Run isolated native case',conclusion:'success'},{name:'Retain isolated worker evidence',conclusion:'success'}]},
          artifact:{id:2000+index,name:'comparison-worker-'+cell.id,sizeInBytes:1,digest:'sha256:'+'d'.repeat(64),expired:false},
          workerSha256:'c'.repeat(64)}));
        writeFileSync(process.argv[1],JSON.stringify(plan));
        writeFileSync(process.argv[2],JSON.stringify({schemaVersion:1,cohort,cells}));
        """;
    private const string InvalidWorkersSource = """
        import {readFileSync,mkdirSync,writeFileSync} from 'node:fs';
        import {join} from 'node:path';
        const plan=JSON.parse(readFileSync(process.argv[1],'utf8'));
        for (const cell of plan.cells) {
          const directory=join(process.argv[2],'workers',cell.id);
          mkdirSync(directory,{recursive:true});
          writeFileSync(join(directory,'worker.json'),'{}');
        }
        """;

    private IsolatedAggregateCliFixture(string root)
    {
        Root = root;
        Input = Path.Combine(root, InputName);
        Output = Path.Combine(root, OutputName);
        Plan = Path.Combine(root, PlanFile);
        Proof = Path.Combine(root, ProofFile);
    }

    internal string Root { get; }
    internal string Input { get; }
    internal string Output { get; }
    internal string Plan { get; }
    internal string Proof { get; }

    internal static async Task<IsolatedAggregateCliFixture> CreateAsync(CancellationToken token)
    {
        var fixture = new IsolatedAggregateCliFixture(Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(fixture.Input);
        try
        {
            var result = await IsolatedAggregateNodeProcess.RunAsync(
                [InputMode, EvalMode, PrepareSource, fixture.Plan, fixture.Proof, IsolatedAggregateNodeProcess.Module(PlanModule)], token);
            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.Error).IsEqualTo(string.Empty);
            return fixture;
        }
        catch (Exception)
        {
            fixture.Dispose();
            throw;
        }
    }

    internal Task<IsolatedAggregateNodeResult> RunAsync(CancellationToken token, string? input = null, string? output = null)
        => IsolatedAggregateNodeProcess.RunAsync(
            [IsolatedAggregateNodeProcess.Module(CliModule), $"--input={input ?? Input}",
                $"--output={output ?? Output}", $"--plan={Plan}", $"--proof={Proof}"], token);

    internal async Task<JsonObject> ReadProofAsync(CancellationToken token)
        => JsonNode.Parse(await File.ReadAllTextAsync(Proof, token))!.AsObject();

    internal Task WriteProofAsync(JsonObject value, CancellationToken token)
        => File.WriteAllTextAsync(Proof, value.ToJsonString(), token);

    internal async Task CreateInvalidWorkerFilesAsync(CancellationToken token)
    {
        var response = await IsolatedAggregateNodeProcess.RunAsync(
            [InputMode, EvalMode, InvalidWorkersSource, Plan, Input], token);
        await Assert.That(response.ExitCode).IsEqualTo(0);
        await Assert.That(response.Error).IsEqualTo(string.Empty);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
