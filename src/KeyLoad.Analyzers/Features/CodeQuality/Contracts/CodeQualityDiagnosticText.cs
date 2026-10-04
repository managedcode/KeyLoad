namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class CodeQualityDiagnosticCategories
{
    public const string Architecture = "Architecture";
    public const string Design = "Design";
    public const string Reliability = "Reliability";
}

internal static class CodeQualityDiagnosticText
{
    public const string LiteralMachineKeyTitle = "Machine keys must use named constants";
    public const string LiteralMachineKeyMessage = "Machine key '{0}' must be referenced through a named const string";
    public const string LiteralMachineKeyDescription = "Dictionary, JSON, configuration, header, query, metadata, and serialization keys must not be inline string literals.";

    public const string EndpointMappingTitle = "Endpoint mappings belong to host endpoint extensions";
    public const string EndpointMappingMessage = "Program.cs must call only '{0}'; move direct endpoint mapping '{1}' into the host endpoint extension";
    public const string EndpointMappingDescription = "KeyLoad.Server Program.cs is a composition root. Concrete Map* endpoint registrations belong to the MapKeyLoadApi host endpoint extension.";

    public const string GrainVersionTitle = "Grain interfaces require explicit versions";
    public const string GrainVersionMessage = "Grain interface '{0}' must declare a positive Orleans VersionAttribute";
    public const string GrainVersionDescription = "Explicit grain interface versions prevent new invokable contracts from being routed to incompatible silos during rolling deployments. Increment the version whenever a public method is added and never change an existing method signature.";

    public const string CompositionRootTitle = "Program files are thin composition roots";
    public const string CompositionRootMessage = "Program.cs may only create, compose, build, and run the host through aggregate application methods; move '{0}' into an app-owned Hosting extension or typed runner";
    public const string CompositionRootDescription = "Executable Program.cs files must not own concrete registrations, configuration, resources, middleware, endpoints, helpers, or business behavior.";

    public const string OrleansConstructorTitle = "Orleans DTOs must not declare instance constructors";
    public const string OrleansConstructorMessage = "Orleans DTO '{0}' must not declare an instance constructor; use property initialization or a static factory method";
    public const string OrleansConstructorDescription = "Orleans wire, request, response, event, option, and state DTOs remain serializer-friendly data shapes. Put controlled creation behind a static factory instead of an explicit instance constructor.";

    public const string SystemClockTitle = "Use TimeProvider for current time";
    public const string SystemClockMessage = "Use TimeProvider instead of direct system clock access '{0}'";
    public const string SystemClockDescription = "Current time must come from TimeProvider so runtime behavior and tests use one explicit, controllable clock abstraction.";

    public const string OrleansSerializerTitle = "Orleans DTOs require generated serialization";
    public const string OrleansSerializerMessage = "Orleans DTO '{0}' must declare GenerateSerializer";
    public const string OrleansSerializerDescription = "Every KeyLoad-owned grain argument, result, nested transport type, persistent state, transactional state, or type declaring Orleans Id members must use Orleans source-generated serialization.";

    public const string UntypedCatchTitle = "Declare the caught exception type";
    public const string UntypedCatchMessage = "Declare an exception type, such as catch (Exception), instead of an untyped catch";
    public const string UntypedCatchDescription = "Catch clauses must explicitly declare System.Exception or a more specific exception type.";
}

internal static class CodeQualitySourceNames
{
    public const string ProgramFile = "Program.cs";
    public const string ProgramType = "Program";
    public const string ProgramLineReplacement = " ";
    public const string TruncationEllipsis = "...";
    public const string MapMethodPrefix = "Map";
    public const string OrleansNamespace = "Orleans";
    public const string ContractsDirectorySegment = "/Contracts/";
    public const string SystemNamespace = "System";
}

internal static class CodeQualityLifecycleMethodNames
{
    public const string CreateBuilder = "CreateBuilder";
    public const string CreateDefault = "CreateDefault";
    public const string Build = "Build";
    public const string Run = "Run";
    public const string RunAsync = "RunAsync";
}

internal static class CodeQualitySourceLimits
{
    public const int DiagnosticExcerptLength = 96;
    private const int TruncationEllipsisLength = 3;
    public const int DiagnosticExcerptPrefixLength =
        DiagnosticExcerptLength - TruncationEllipsisLength;
}
