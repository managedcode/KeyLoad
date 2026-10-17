using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class FunctionalTestParallelismTests
{
    private const string ParallelismSetting = TestExecutionOptions.SectionName + ":" + nameof(TestExecutionOptions.MaximumParallelTests);
    private const string SelectedFilter = "/*/*/FunctionalCase/*";
    private const string UnitSuite = "unit";
    private const string UnitProject = "KeyLoad.UnitTests";
    private const int DefaultParallelism = 50;
    private const int Explicit20Parallelism = 20;
    private const int TunedParallelism = 50;

    [Test]
    [Arguments(null, DefaultParallelism)]
    [Arguments("20", Explicit20Parallelism)]
    [Arguments("50", TunedParallelism)]
    public async Task AcTunitEntry013TypedFunctionalSelectionPreservesSuiteAndExplicitParallelism(string? selected, int expected)
    {
        using var configuration = CreateSelection();
        if (selected is not null)
        {
            configuration[ParallelismSetting] = selected;
        }
        var original = configuration.AsEnumerable().ToArray();
        var settings = TestSuiteSettings.Read(configuration)!;
        await Assert.That(settings.MaximumParallelTests).IsEqualTo(expected);
        await Assert.That(settings.Project).IsEqualTo(UnitProject);
        await Assert.That(settings.Filter).IsEqualTo(SelectedFilter);
        await Assert.That(settings.Timeout).IsEqualTo(TimeSpan.FromMinutes(30));
        await Assert.That(configuration.AsEnumerable().ToArray()).IsEquivalentTo(original);
    }

    [Test]
    [Arguments("51")]
    [Arguments("0")]
    [Arguments("-1")]
    public async Task AcTunitEntry013UnsupportedFunctionalParallelismRejectsThenHealthySelectionContinues(string rejected)
    {
        using var configuration = CreateSelection();
        configuration[ParallelismSetting] = rejected;
        var original = configuration.AsEnumerable().ToArray();
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => TestSuiteSettings.Read(configuration));
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(TestExecutionOptions));
        await Assert.That(configuration.AsEnumerable().ToArray()).IsEquivalentTo(original);
        configuration[ParallelismSetting] = DefaultParallelism.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var healthy = TestSuiteSettings.Read(configuration)!;
        await Assert.That(healthy.MaximumParallelTests).IsEqualTo(DefaultParallelism);
        await Assert.That(healthy.Filter).IsEqualTo(SelectedFilter);
        await Assert.That(healthy.Project).IsEqualTo(UnitProject);
    }

    private static ConfigurationManager CreateSelection()
    {
        var configuration = new ConfigurationManager();
        configuration[TestSuiteSettings.SuiteSetting] = UnitSuite;
        configuration[TestSuiteSettings.FilterSetting] = SelectedFilter;
        return configuration;
    }
}
