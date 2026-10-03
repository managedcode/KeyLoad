using KeyLoad.UnitTests.Features.RepositoryGovernance;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class NativeSerializationWorkflowCredentialTests
{
    private const string TokenBinding = "          GH_TOKEN: " + NativeSerializationWorkflowCredentialSource.Token + "\n";
    private const string CorpusBinding = "          KEYLOAD_NATIVE_SERIALIZATION_CORPUS_DIRECTORY: "
        + "${{ github.workspace }}/artifacts/native-serialization/corpus\n";
    private const string Normal = "Test native diagnostic contracts and generated consumers";
    private const string Scalar = "Test diagnostic contracts without hardware intrinsics";

    [Test]
    public async Task R18Ac001RealWorkflowAuthenticatesOnlyPrepareAndVerify()
    {
        var workflow = WorkflowLayoutSource.Read(NativeSerializationWorkflowCredentialSource.Workflow);
        await Assert.That(NativeSerializationWorkflowCredentialSource.Validate(workflow)).IsEqualTo(2);
    }

    [Test]
    [Arguments("workflow")]
    [Arguments("job")]
    [Arguments("workflow-env-space")]
    [Arguments("job-env-space")]
    [Arguments("workflow-env-quoted")]
    [Arguments("job-env-quoted")]
    [Arguments("workflow-env-anchored")]
    [Arguments("job-env-anchored")]
    [Arguments("workflow-other-token")]
    [Arguments("job-other-token")]
    [Arguments("measurement-other-token")]
    [Arguments("measurement-whitespace-token")]
    [Arguments("normal-secret-binding")]
    [Arguments("measurement-secret-whitespace")]
    [Arguments("measurement")]
    [Arguments("normal")]
    [Arguments("scalar")]
    [Arguments("prepare-missing")]
    [Arguments("verify-missing")]
    [Arguments("wrong-credential")]
    [Arguments("duplicate-credential")]
    [Arguments("alternate-token-name")]
    [Arguments("moved-credential")]
    [Arguments("flow-map")]
    public async Task R18Ac001RealWorkflowCredentialRegressionsAreRejected(string mutation)
    {
        var workflow = WorkflowLayoutSource.Read(NativeSerializationWorkflowCredentialSource.Workflow);
        var mutated = Mutate(workflow, mutation);
        await Assert.That(mutated == workflow).IsFalse();
        await Assert.That(Accepts(mutated)).IsFalse();
    }

    private static string Mutate(string workflow, string mutation)
    {
        if (mutation.StartsWith("workflow", StringComparison.Ordinal))
        {
            return workflow.Replace("\njobs:\n", "\n" + EnvironmentKey(mutation) + "\n"
                + InsertedBinding(mutation, 2) + "jobs:\n", StringComparison.Ordinal);
        }

        if (mutation.StartsWith("job", StringComparison.Ordinal))
        {
            var job = WorkflowLayoutSource.JobBlock(workflow, NativeSerializationWorkflowCredentialSource.Job);
            var mutated = job.Replace("    steps:\n", "    " + EnvironmentKey(mutation) + "\n"
                + InsertedBinding(mutation, 6) + "    steps:\n", StringComparison.Ordinal);
            return workflow.Replace(job, mutated, StringComparison.Ordinal);
        }

        var name = mutation switch
        {
            "measurement" or "moved-credential" or "measurement-other-token" or "measurement-whitespace-token"
                or "measurement-secret-whitespace" => NativeSerializationWorkflowCredentialSource.Measurement,
            "normal" or "normal-secret-binding" => Normal,
            "scalar" => Scalar,
            "verify-missing" => NativeSerializationWorkflowCredentialSource.Verify,
            _ => NativeSerializationWorkflowCredentialSource.Prepare
        };
        var step = NativeSerializationWorkflowCredentialSource.Step(workflow, name);
        var changed = MutateStep(step, mutation);
        var result = workflow.Replace(step, changed, StringComparison.Ordinal);
        return mutation == "moved-credential"
            ? RemoveCredential(result, NativeSerializationWorkflowCredentialSource.Verify) : result;
    }

    private static string EnvironmentKey(string mutation)
        => mutation.EndsWith("-space", StringComparison.Ordinal) ? "env :"
            : mutation.EndsWith("-quoted", StringComparison.Ordinal) ? "'env':"
            : mutation.EndsWith("-anchored", StringComparison.Ordinal) ? "&credential_key env:" : "env:";

    private static string MutateStep(string step, string mutation) => mutation switch
    {
        "prepare-missing" or "verify-missing" => step.Replace(TokenBinding, string.Empty, StringComparison.Ordinal),
        "wrong-credential" => step.Replace(NativeSerializationWorkflowCredentialSource.Token,
            "${{ secrets.NativeDiagnosticToken }}", StringComparison.Ordinal),
        "alternate-token-name" => step.Replace("GH_TOKEN:", "GITHUB_TOKEN:", StringComparison.Ordinal),
        "flow-map" => step.Replace("        env:\n" + TokenBinding + CorpusBinding,
            "        env: {GH_TOKEN: '" + NativeSerializationWorkflowCredentialSource.Token
                + "', KEYLOAD_NATIVE_SERIALIZATION_CORPUS_DIRECTORY: '${{ github.workspace }}/artifacts/native-serialization/corpus'}\n",
            StringComparison.Ordinal),
        _ => AddCredential(step, mutation)
    };

    private static string AddCredential(string step, string mutation)
    {
        const string environment = "        env:\n";
        var binding = InsertedBinding(mutation, 10);
        if (step.Contains(environment, StringComparison.Ordinal))
        {
            return step.Replace(environment, environment + binding, StringComparison.Ordinal);
        }

        var firstLine = step.IndexOf('\n', StringComparison.Ordinal) + 1;
        return step.Insert(firstLine, environment + binding);
    }

    private static string InsertedBinding(string mutation, int indent)
    {
        var alternate = mutation is "workflow-other-token" or "job-other-token" or "measurement-other-token"
            or "measurement-whitespace-token" or "normal-secret-binding" or "measurement-secret-whitespace";
        var value = mutation switch
        {
            "measurement-whitespace-token" => "${{ github . token }}",
            "normal-secret-binding" => "${{ secrets.NativeDiagnosticToken }}",
            "measurement-secret-whitespace" => "${{ secrets [ 'NativeDiagnosticToken' ] }}",
            _ => NativeSerializationWorkflowCredentialSource.Token
        };
        return new string(' ', indent) + (alternate ? "OTHER_TOKEN" : "GH_TOKEN") + ": " + value + "\n";
    }

    private static string RemoveCredential(string workflow, string name)
    {
        var step = NativeSerializationWorkflowCredentialSource.Step(workflow, name);
        return workflow.Replace(step, step.Replace(TokenBinding, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    private static bool Accepts(string workflow)
    {
        try
        {
            NativeSerializationWorkflowCredentialSource.Validate(workflow);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
