namespace KeyLoad.Comparisons;

internal static class ComparisonValidation
{
    private const string QueueMismatch = "QueueMissingDuplicateOrWrongPayload";
    private const string WriteMismatch = "WriteReadbackMismatch";
    private const string EventMismatch = "EventIdentityRevisionOrPayloadMismatch";
    private const string PointMismatch = "PointReadMismatch";
    private const string WarmupQueueMismatch = "WarmupQueueMismatch";
    private const string VectorMismatch = "ExactRecallOrProjectionMismatch";
    private const string GraphMismatch = "GraphReachabilityMismatch";

    public static async Task ValidateBatchAsync(IComparisonSession reader, BenchmarkDataset dataset, Scenario scenario,
        BenchmarkDocument[] inputs, OperationSample[] samples, OperationResult?[] outputs, CancellationToken cancellationToken)
    {
        var expectedMessages = scenario == Scenario.QueueCycle ? inputs.ToDictionary(document => document.Id, StringComparer.Ordinal) : [];
        var completedMessages = new HashSet<string>(StringComparer.Ordinal);
        for (var operation = 0; operation < samples.Length; operation++)
        {
            if (!samples[operation].Success)
            {
                continue;
            }

            try
            {
                using var deadline = ComparisonDeadline.Create(dataset.Options.TimeoutSeconds, cancellationToken);
                var output = outputs[operation]!;
                if (scenario == Scenario.QueueCycle)
                {
                    ValidateQueue(output, expectedMessages, completedMessages);
                }
                else
                {
                    await ValidateOperationAsync(reader, dataset, scenario, inputs[operation], output, deadline.Token);
                }
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            { samples[operation] = samples[operation] with { Success = false, Error = ComparisonErrors.Safe(error) }; }
        }
    }

    private static void ValidateQueue(OperationResult output, Dictionary<string, BenchmarkDocument> expectedMessages,
        HashSet<string> completedMessages)
    {
        if (output.Message is null || !expectedMessages.TryGetValue(output.Message.Id, out var expected)
            || !BenchmarkDataset.SameDocument(output.Message, expected) || !completedMessages.Add(output.Message.Id))
        {
            throw new ComparisonFailureException(QueueMismatch);
        }
    }

    public static async Task ValidateOperationAsync(IComparisonSession reader, BenchmarkDataset dataset,
        Scenario scenario, BenchmarkDocument input, OperationResult output, CancellationToken cancellationToken)
    {
        if (scenario == Scenario.DocumentWrite)
        {
            if (!BenchmarkDataset.SameDocument(await reader.ReadAsync(input, cancellationToken), input))
            {
                throw new ComparisonFailureException(WriteMismatch);
            }
            return;
        }
        if (ComparisonMutationPreparation.Required(scenario))
        {
            ComparisonMutationOracle.RequireFinalState(scenario, await reader.ReadAsync(input, cancellationToken), input);
            return;
        }
        if (scenario == Scenario.StreamAppend)
        {
            if (!BenchmarkDataset.SameEvent(await reader.ReadEventAsync(input, cancellationToken), input))
            {
                throw new ComparisonFailureException(EventMismatch);
            }
            return;
        }
        ValidateResult(scenario, input, output, dataset);
    }

    private static void ValidateResult(Scenario scenario, BenchmarkDocument input, OperationResult output, BenchmarkDataset dataset)
    {
        if (scenario == Scenario.PointRead && !BenchmarkDataset.SameDocument(output.Document, input))
        {
            throw new ComparisonFailureException(PointMismatch);
        }

        if (scenario == Scenario.QueueCycle && !BenchmarkDataset.SameDocument(output.Message, input))
        {
            throw new ComparisonFailureException(WarmupQueueMismatch);
        }

        if (scenario == Scenario.StreamRead && !BenchmarkDataset.SameEvent(output.Event, input))
        {
            throw new ComparisonFailureException(EventMismatch);
        }

        if (scenario == Scenario.VectorExact)
        {
            var expected = dataset.ExactNeighbors(input);
            if (output.Neighbors is not { } neighbors || neighbors.IsDefault
                || !neighbors.Select(item => item.Id).SequenceEqual(expected.Select(item => item.Id))
                || neighbors.Where((document, index) => !BenchmarkDataset.SameJson(document.Json, expected[index].Json)).Any())
            {
                throw new ComparisonFailureException(VectorMismatch);
            }
        }
        if (scenario is Scenario.GraphNeighbors or Scenario.GraphTraverse)
        {
            var expected = dataset.Reachable(input, scenario == Scenario.GraphNeighbors ? 1 : dataset.Options.GraphDepth);
            if (output.Vertices is not { } vertices || vertices.IsDefault
                || !vertices.SequenceEqual(expected, StringComparer.Ordinal))
            {
                throw new ComparisonFailureException(GraphMismatch);
            }
        }
    }
}
