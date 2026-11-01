namespace KeyLoad.ClusterTests.DatabaseComposition;

/// <summary>Independent creation-identity oracle over the original caller-owned command ID.</summary>
internal static class FunctionalGateCreationLifetime
{
    internal static void Expect(FunctionalGateContext context, Guid commandId, ResourceDefinition definition)
        => context.ReferenceBindings.ExpectCreation(context.Partition, definition.Name, Create(commandId));

    internal static EntityRefLifetime Create(Guid commandId)
    {
        if (commandId == Guid.Empty)
        {
            throw new InvalidOperationException("The original public creation command identity is required.");
        }
        return new(commandId.ToString("N"));
    }
}
