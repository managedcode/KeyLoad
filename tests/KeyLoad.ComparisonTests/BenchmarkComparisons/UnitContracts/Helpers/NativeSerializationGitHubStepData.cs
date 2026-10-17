using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Controlled states exercise classification and cannot authenticate a benchmark run.</summary>
internal static class NativeSerializationGitHubStepData
{
    internal static JsonObject Input(bool measured)
    {
        var names = new[] { NativeSerializationGitHubStepFields.Build, NativeSerializationGitHubStepFields.Normal,
            NativeSerializationGitHubStepFields.Scalar, NativeSerializationGitHubStepFields.Record,
            NativeSerializationGitHubStepFields.Measure };
        var steps = new JsonArray();
        foreach (var name in names.Take(measured ? 5 : 3))
        {
            steps.Add(new JsonObject
            {
                [NativeSerializationGitHubStepFields.Name] = name,
                [NativeSerializationGitHubStepFields.Status] = "completed",
                [NativeSerializationGitHubStepFields.Conclusion] = "success"
            });
        }

        return new JsonObject
        {
            [NativeSerializationGitHubStepFields.Job] = new JsonObject { [NativeSerializationGitHubStepFields.Steps] = steps },
            [NativeSerializationGitHubStepFields.Measured] = measured
        };
    }

    internal static JsonArray Steps(JsonObject input)
        => input[NativeSerializationGitHubStepFields.Job]![NativeSerializationGitHubStepFields.Steps]!.AsArray();

    internal static void State(JsonNode step, string status, string? conclusion)
    {
        step[NativeSerializationGitHubStepFields.Status] = status;
        step[NativeSerializationGitHubStepFields.Conclusion] = conclusion;
    }

    internal static void Fault(JsonObject input, string fault)
    {
        var job = input[NativeSerializationGitHubStepFields.Job]!.AsObject();
        var steps = Steps(input);
        if (!ShapeFault(input, job, steps, fault) && !StateFault(steps, fault))
        {
            throw new ArgumentOutOfRangeException(nameof(fault), fault, "Unknown controlled step defect.");
        }
    }

    private static bool ShapeFault(JsonObject input, JsonObject job, JsonArray steps, string fault)
    {
        switch (fault)
        {
            case "null-job":
                input[NativeSerializationGitHubStepFields.Job] = null;
                return true;
            case "missing-steps":
                job.Remove(NativeSerializationGitHubStepFields.Steps);
                return true;
            case "null-steps":
                job[NativeSerializationGitHubStepFields.Steps] = null;
                return true;
            case "object-steps":
                job[NativeSerializationGitHubStepFields.Steps] = new JsonObject();
                return true;
            case "missing-step":
                steps.RemoveAt(0);
                return true;
            case "null-step":
                steps[0] = null;
                return true;
            case "duplicate-step":
                steps.Add(steps[0]!.DeepClone());
                return true;
            case "missing-name":
                steps[0]!.AsObject().Remove(NativeSerializationGitHubStepFields.Name);
                return true;
            case "wrong-name":
                steps[0]![NativeSerializationGitHubStepFields.Name] = "other";
                return true;
            default:
                return false;
        }
    }

    private static bool StateFault(JsonArray steps, string fault)
    {
        switch (fault)
        {
            case "missing-status":
                steps[0]!.AsObject().Remove(NativeSerializationGitHubStepFields.Status);
                return true;
            case "missing-conclusion":
                steps[0]!.AsObject().Remove(NativeSerializationGitHubStepFields.Conclusion);
                return true;
            case "pending-then-failed":
                State(steps[0]!, "in_progress", null);
                State(steps[steps.Count - 1]!, "completed", "failure");
                return true;
            case "pending-then-missing":
                State(steps[0]!, "queued", null);
                steps.RemoveAt(steps.Count - 1);
                return true;
            case "pending-then-duplicate":
                State(steps[0]!, "queued", null);
                steps.Add(steps[steps.Count - 1]!.DeepClone());
                return true;
            default:
                return false;
        }
    }
}
