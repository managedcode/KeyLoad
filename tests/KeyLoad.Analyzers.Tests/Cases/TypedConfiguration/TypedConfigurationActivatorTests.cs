namespace KeyLoad.Analyzers.Tests.CodeQuality;

/// <summary>AC-CQ-035/037: known execution reflection construction cannot bypass owned options.</summary>
internal sealed class TypedConfigurationActivatorTests
{
    [Test]
    [Arguments("[|System.Activator.CreateInstance<Policy>()|]")]
    [Arguments("[|System.Activator.CreateInstance(typeof(Policy))|]")]
    [Arguments("[|RuntimeActivator.CreateInstance(type: typeof(Policy))|]")]
    public async Task ActivatorRejectsActualKnownOptionsTypesAsync(string expression)
    {
        var source = $$"""
            using RuntimeActivator = System.Activator;
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int Capacity { get; init; } = 64; }
            internal static class Subject
            {
                internal static object Execute() => {{expression}};
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task CentralBindingOwnsKnownOptionsReflectionConstructionAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int Capacity { get; init; } = 64; }
            [KeyLoad.ConfigurationBinding]
            internal static class Composition
            {
                internal static Policy Create() => System.Activator.CreateInstance<Policy>();
                internal static object Inspect() => System.Activator.CreateInstance(typeof(Policy));
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task DomainObjectsUnknownReflectionTypesAndOpenGenericFactoriesAreNotOptionsProofAsync()
    {
        const string source = """
            internal sealed class Payload { }
            internal static class Subject
            {
                internal static Payload Domain() => System.Activator.CreateInstance<Payload>();
                internal static object Inspect() => System.Activator.CreateInstance(typeof(Payload));
                internal static object Unknown(System.Type type) => System.Activator.CreateInstance(type);
                internal static T Create<T>() where T : new() => new T();
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
