using System.Text.Json.Nodes;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Controlled states exercise classification and cannot authenticate a benchmark run.</summary>
internal static class SerializationGitHubStepData
{
    internal static JsonObject Input(bool measured)
    {
        var names = new[] { SerializationGitHubStepFields.Build, SerializationGitHubStepFields.Normal,
            SerializationGitHubStepFields.Scalar, SerializationGitHubStepFields.Record,
            SerializationGitHubStepFields.Measure };
        var steps = new JsonArray();
        foreach (var name in names.Take(measured ? 5 : 3))
        {
            steps.Add(new JsonObject
            {
                [SerializationGitHubStepFields.Name] = name,
                [SerializationGitHubStepFields.Status] = "completed",
                [SerializationGitHubStepFields.Conclusion] = "success"
            });
        }

        return new JsonObject
        {
            [SerializationGitHubStepFields.Job] = new JsonObject { [SerializationGitHubStepFields.Steps] = steps },
            [SerializationGitHubStepFields.Measured] = measured
        };
    }

    internal static JsonArray Steps(JsonObject input)
        => input[SerializationGitHubStepFields.Job]![SerializationGitHubStepFields.Steps]!.AsArray();

    internal static void State(JsonNode step, string status, string? conclusion)
    {
        step[SerializationGitHubStepFields.Status] = status;
        step[SerializationGitHubStepFields.Conclusion] = conclusion;
    }

    internal static void Fault(JsonObject input, string fault)
    {
        var job = input[SerializationGitHubStepFields.Job]!.AsObject();
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
                input[SerializationGitHubStepFields.Job] = null;
                return true;
            case "missing-steps":
                job.Remove(SerializationGitHubStepFields.Steps);
                return true;
            case "null-steps":
                job[SerializationGitHubStepFields.Steps] = null;
                return true;
            case "object-steps":
                job[SerializationGitHubStepFields.Steps] = new JsonObject();
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
                steps[0]!.AsObject().Remove(SerializationGitHubStepFields.Name);
                return true;
            case "wrong-name":
                steps[0]![SerializationGitHubStepFields.Name] = "other";
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
                steps[0]!.AsObject().Remove(SerializationGitHubStepFields.Status);
                return true;
            case "missing-conclusion":
                steps[0]!.AsObject().Remove(SerializationGitHubStepFields.Conclusion);
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
