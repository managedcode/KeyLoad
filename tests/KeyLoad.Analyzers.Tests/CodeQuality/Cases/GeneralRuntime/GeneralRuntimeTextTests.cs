namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-033/037: general string, character and interpolation contexts require identities.</summary>
internal sealed class GeneralRuntimeTextTests
{
    [Test]
    public async Task NativeFriendAssemblyIdentityIsCompilerMetadataAsync()
    {
        const string source = """
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("KeyLoad.Analyzers.Tests")]
            internal sealed class Subject { }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    public async Task AnUnrelatedAttributeDoesNotBorrowTheCompilerAssemblyIdentityBoundaryAsync()
    {
        const string source = """
            [FriendAssembly([|"KeyLoad.Analyzers.Tests"|])]
            internal sealed class Subject { }
            internal sealed class FriendAssemblyAttribute(string identity) : System.Attribute
            {
                internal string Identity { get; } = identity;
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    public async Task AMarkedOptionsConstructorCannotHideExecutableMessageTextAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy
            {
                internal string Endpoint { get; }
                internal Policy()
                {
                    Endpoint = "localhost";
                    System.Console.WriteLine([|"Runtime diagnostic message"|]);
                }
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    [Arguments("return [|\"caller payload\"|];")]
    [Arguments("return [|\"\"|];")]
    [Arguments("return [|'x'|];")]
    [Arguments("return [|@\"raw\\text\"|];")]
    [Arguments("return [|\"\"\"raw\"\"\"|];")]
    [Arguments("return [|\"native\"u8|].ToArray();")]
    public async Task ReturnAndUtf8PayloadLiteralsRequireNamedIdentitiesAsync(string operation)
    {
        var source = $$"""
            using System;
            internal static class Subject
            {
                internal static object Execute() { {{operation}} }
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    public async Task InterpolationTextAndFormatTokensKeepTheirFullSourceSpansAsync()
    {
        const string source = """
            internal static class Subject
            {
                internal static string Execute(int value) => $"[|prefix |]{value:[|X8|]}[| suffix|]";
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    public async Task ConstantInterpolationChunksAndNameofPreserveRuntimeCompositionAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const string Prefix = "prefix ";
                private const string Suffix = " suffix";
                internal static string Execute(int value) => $"{Prefix}{value}{Suffix}" + nameof(Execute);
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    public async Task OrdinaryAttributeNamesRequireConstantsAndNumericMetadataIsPreservedAsync()
    {
        const string source = """
            [System.Obsolete([|"Use the configured operation."|])]
            internal sealed class Subject
            {
                [System.ComponentModel.DefaultValue(1)] public int Value { get; set; }
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
        await MagicRuntimeFixture.AssertDurationAsync(source.Replace("[|", "", StringComparison.Ordinal)
            .Replace("|]", "", StringComparison.Ordinal));
    }

    [Test]
    public async Task ActualMarkedOptionsDefaultTextHasAnExplicitDefinitionOwnerAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class OperationSettings
            {
                internal string Endpoint { get; set; } = "localhost";
                internal char Separator { get; set; } = '/';
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }
}
