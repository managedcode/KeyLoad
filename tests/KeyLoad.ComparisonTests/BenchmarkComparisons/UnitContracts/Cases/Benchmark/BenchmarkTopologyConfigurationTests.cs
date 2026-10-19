using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Startup keeps native silo count separate from the required number of persisted copies.</summary>
internal sealed class BenchmarkTopologyConfigurationTests
{
    private const string ConfigurationSection = "KeyLoad";
    private const string SiloSetting = "KeyLoad:SiloCount";
    private const string CopySetting = "KeyLoad:ReplicaCount";

    [Test]
    [Arguments(1, 1)]
    [Arguments(2, 1)]
    [Arguments(2, 2)]
    [Arguments(3, 2)]
    [Arguments(5, 3)]
    [Arguments(5, 5)]
    [Arguments(64, 2)]
    public async Task IndependentPositiveSiloAndCopyCountsAreAccepted(int silos, int copies)
    {
        var options = BenchmarkTopologyConfigurationFixture.Node(silos, copies);
        options.Validate();
        var configured = new ReplicaCopyOptions { SiloCount = silos, ReplicaCount = copies };
        configured.Validate();
        await Assert.That(options.SiloCount).IsEqualTo(silos);
        await Assert.That(options.ReplicaCount).IsEqualTo(copies);
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(1, 0)]
    [Arguments(1, 2)]
    [Arguments(2, 3)]
    [Arguments(-1, 1)]
    [Arguments(3, -1)]
    public void InvalidCountsAreRejectedBeforeOpeningStorage(int silos, int copies)
    {
        var options = BenchmarkTopologyConfigurationFixture.Node(silos, copies);
        var configured = new ReplicaCopyOptions { SiloCount = silos, ReplicaCount = copies };
        Assert.ThrowsExactly<InvalidOperationException>(options.Validate);
        Assert.ThrowsExactly<OptionsValidationException>(configured.Validate);
    }

    [Test]
    public void NativeHostingAndCredentialValidationRemainRequired()
    {
        var options = BenchmarkTopologyConfigurationFixture.Node(3, 2);
        NodeOptions[] invalid =
        [
            options with { DataDirectory = string.Empty },
            options with { SiloName = string.Empty },
            options with { SiloAddress = string.Empty },
            options with { ClusterId = string.Empty },
            options with { Incarnation = Guid.Empty },
            options with { SigningKey = string.Empty },
            options with { AdminKey = string.Empty },
            options with { SiloPort = 0 }
        ];
        foreach (var item in invalid)
        {
            Assert.ThrowsExactly<InvalidOperationException>(item.Validate);
        }
        options.Validate();
    }

    [Test]
    public async Task BothCountsBindFromTheirIndependentStartupSettings()
    {
        using var configuration = new ConfigurationManager();
        configuration[SiloSetting] = "5";
        configuration[CopySetting] = "2";
        var selected = configuration.GetSection(ConfigurationSection).Get<NodeOptions>()!;
        await Assert.That(selected.SiloCount).IsEqualTo(5);
        await Assert.That(selected.ReplicaCount).IsEqualTo(2);
        var ordinary = new NodeOptions();
        await Assert.That(ordinary.SiloCount).IsEqualTo(1);
        await Assert.That(ordinary.ReplicaCount).IsEqualTo(1);
    }
}
