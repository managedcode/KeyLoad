namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class CodeQualityDiagnosticText
{
    public const string MagicRuntimeDurationTitle = "Runtime numeric literals must use named constants";
    public const string MagicRuntimeDurationMessage = "Runtime numeric literal '{0}' must use a domain-named constant or configured option";
    public const string MagicRuntimeDurationDescription = "Explicit runtime numeric literals other than zero and one require named immutable identities or typed options; metadata identifiers and named constant declarations are preserved.";
    public const string MagicRuntimeStringTitle = "Runtime text literals must use named identities";
    public const string MagicRuntimeStringMessage = "Runtime text literal {0} must use a domain-named constant or nameof";
    public const string MagicRuntimeStringDescription = "Runtime strings, characters, endpoint and method tokens, and interpolation text require named identities; existing machine-key diagnostics retain ownership.";
    public const string TypedConfigurationTitle = "Runtime policy must use centrally validated typed options";
    public const string TypedConfigurationMessage = "'{0}' must use centrally bound and validated IOptions<T>; raw reads and hardcoded operational policy belong only to their explicit configuration owners";
    public const string TypedConfigurationDescription = "Actual configuration ownership metadata separates central binding/default definitions from feature execution; execution consumes IOptions<T> and never hides operational deadlines behind constants or static readonly values.";
    public const string LiteralMachineKeyTitle = "Machine keys must use named constants";
    public const string LiteralMachineKeyMessage = "Machine key '{0}' must be referenced through a named const string";
    public const string LiteralMachineKeyDescription = "Dictionary, JSON, configuration, header, query, metadata, and serialization keys must not be inline string literals.";

    public const string EndpointMappingTitle = "Endpoint mappings belong to host endpoint extensions";
    public const string EndpointMappingMessage = "Program.cs must call only '{0}'; move direct endpoint mapping '{1}' into the host endpoint extension";
    public const string EndpointMappingDescription = "KeyLoad Program.cs is a composition root. Concrete Map* endpoint registrations belong to the MapKeyLoadApi host endpoint extension.";

    public const string GrainVersionTitle = "Grain interfaces require explicit versions";
    public const string GrainVersionMessage = "Grain interface '{0}' must declare a positive Orleans VersionAttribute";
    public const string GrainVersionDescription = "Grain interfaces declare a positive Orleans version. Any version change requires the owner's explicit permission for that specific increase; refactoring and contract changes do not grant permission.";

    public const string CompositionRootTitle = "Program files show process composition";
    public const string CompositionRootMessage = "Program.cs must show process initialization and lifecycle instead of forwarding the entire entry point to '{0}'";
    public const string CompositionRootDescription = "Executable Program.cs files own visible initialization, mode selection, composition and lifecycle. Keep model operations in their owners and avoid forwarding-only Application wrappers.";

    public const string OrleansConstructorTitle = "Orleans DTOs must not declare instance constructors";
    public const string OrleansConstructorMessage = "Orleans DTO '{0}' must not declare an instance constructor; use property initialization or a static factory method";
    public const string OrleansConstructorDescription = "Orleans wire, request, response, event, option, and state DTOs remain serializer-friendly data shapes. Put controlled creation behind a static factory instead of an explicit instance constructor.";

    public const string SystemClockTitle = "Use TimeProvider for clock access";
    public const string SystemClockMessage = "Use TimeProvider instead of direct system clock access '{0}'";
    public const string SystemClockDescription = "Current time and elapsed measurements must come from TimeProvider so runtime behavior and tests use one explicit, controllable clock abstraction.";

    public const string OrleansSerializerTitle = "Orleans DTOs require generated serialization";
    public const string OrleansSerializerMessage = "Orleans DTO '{0}' must declare GenerateSerializer";
    public const string OrleansSerializerDescription = "Every KeyLoad-owned grain argument, result, nested transport type, persistent state, transactional state, or type declaring Orleans Id members must use Orleans source-generated serialization.";

    public const string UntypedCatchTitle = "Declare the caught exception type";
    public const string UntypedCatchMessage = "Declare an exception type, such as catch (Exception), instead of an untyped catch";
    public const string UntypedCatchDescription = "Catch clauses must explicitly declare System.Exception or a more specific exception type.";

    public const string TypedSynchronizationTitle = "Use typed synchronization outside grain-owned state";
    public const string TypedSynchronizationMessage = "Use System.Threading.Lock for short shared-service critical sections and Orleans turn scheduling for activation-owned state instead of '{0}'";
    public const string TypedSynchronizationDescription = "Object-typed locks and Monitor calls are prohibited. Orleans activations use turn scheduling; shared asynchronous work uses cancellation-aware SemaphoreSlim.WaitAsync.";
}
