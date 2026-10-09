using KeyLoad.IntegrationTests.Features.CodeQuality;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>AC-METH-005: broad, disabled or covered native selections fail before acquiring a private RF3 fixture.</summary>
internal sealed class HeavyDocumentLoadAdmissionTests
{
    private const string BroadFilter = "/*/*/*/*";
    private const string OtherFilter = "/*/*/ClusterTests/*";
    private const string CoverageSwitch = "--coverage";
    private const string CoverageSetting = HeavyDocumentLoadAdmission.CoverageSection + ":ServerMode";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void MissingOrDisabledAdmissionRejectsThenExplicitEnabledContinues(bool present)
    {
        using var configuration = new ConfigurationManager();
        if (present)
        { configuration[HeavyDocumentLoadExecutionOptions.EnabledSetting] = bool.FalseString; }
        Assert.ThrowsExactly<OptionsValidationException>(() => HeavyDocumentLoadAdmission.Validate(configuration, HealthyArguments()));
        configuration[HeavyDocumentLoadExecutionOptions.EnabledSetting] = bool.TrueString;
        HeavyDocumentLoadAdmission.Validate(configuration, HealthyArguments());
    }

    [Test]
    [Arguments("broad-filter")]
    [Arguments("foreign-filter")]
    [Arguments("missing-filter")]
    [Arguments("duplicate-filter")]
    [Arguments("nonexclusive")]
    [Arguments("missing-parallelism")]
    [Arguments("duplicate-parallelism")]
    [Arguments("coverage-switch")]
    [Arguments("coverage-preparation")]
    [Arguments("coverage-mode")]
    [Arguments("coverage-setting")]
    public void NativeSelectionRejectsBeforeOwnershipThenHealthyExclusiveCallContinues(string invalid)
    {
        using var configuration = new ConfigurationManager();
        configuration[HeavyDocumentLoadExecutionOptions.EnabledSetting] = bool.TrueString;
        var arguments = HealthyArguments().ToList();
        switch (invalid)
        {
            case "broad-filter": arguments[1] = BroadFilter; break;
            case "foreign-filter": arguments[1] = OtherFilter; break;
            case "missing-filter": arguments.RemoveRange(0, 2); break;
            case "duplicate-filter": arguments.AddRange([HeavyDocumentLoadAdmission.FilterArgument, HeavyDocumentLoadAdmission.ExactFilter]); break;
            case "nonexclusive": arguments[3] = "20"; break;
            case "missing-parallelism": arguments.RemoveRange(2, 2); break;
            case "duplicate-parallelism": arguments.AddRange([HeavyDocumentLoadAdmission.ParallelismArgument, HeavyDocumentLoadAdmission.ExclusiveParallelism]); break;
            case "coverage-switch": arguments.Add(CoverageSwitch); break;
            case "coverage-preparation": configuration[HeavyDocumentLoadAdmission.CoveragePreparationEnvironment] = "[]"; break;
            case "coverage-mode": configuration[NativeCoverageRf3FixtureProtocol.ModeEnvironment] = NativeCoverageRf3FixtureProtocol.Mode; break;
            case "coverage-setting": configuration[CoverageSetting] = NativeCoverageRf3FixtureProtocol.Mode; break;
            default: throw new ArgumentOutOfRangeException(nameof(invalid));
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => HeavyDocumentLoadAdmission.Validate(configuration, arguments));
        using var healthy = new ConfigurationManager();
        healthy[HeavyDocumentLoadExecutionOptions.EnabledSetting] = bool.TrueString;
        HeavyDocumentLoadAdmission.Validate(healthy, HealthyArguments());
    }

    [Test]
    public async Task ExactExclusiveOriginalArgumentsAdmitWithoutChangingTheirIdentity()
    {
        using var configuration = new ConfigurationManager();
        configuration[HeavyDocumentLoadExecutionOptions.EnabledSetting] = bool.TrueString;
        var arguments = HealthyArguments();
        var original = arguments.ToArray();
        HeavyDocumentLoadAdmission.Validate(configuration, arguments);
        await Assert.That(arguments.SequenceEqual(original)).IsTrue();
    }

    private static string[] HealthyArguments() => [HeavyDocumentLoadAdmission.FilterArgument,
        HeavyDocumentLoadAdmission.ExactFilter, HeavyDocumentLoadAdmission.ParallelismArgument, HeavyDocumentLoadAdmission.ExclusiveParallelism];
}
