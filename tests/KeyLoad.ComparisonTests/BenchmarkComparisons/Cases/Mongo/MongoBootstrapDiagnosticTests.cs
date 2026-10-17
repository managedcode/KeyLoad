using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class MongoBootstrapDiagnosticTests
{
    private const string PredicateField = "predicate", HostField = "host", VotingCountField = "votingMembersCount";
    private const string KindField = "kind", ValueField = "value", MembersField = "members", CodeField = "code", CodeNameField = "codeName";
    private const string ProjectionHarness = """
        const fs = require('node:fs');
        const vm = require('node:vm');
        const source = fs.readFileSync(process.argv[1], 'utf8');
        const boundary = source.indexOf('// Native bootstrap composition.');
        if (boundary < 0) throw new Error('MongoDiagnosticBoundaryMissing');
        vm.runInThisContext(source.slice(0, boundary));
        const hosts = ['isolated-mongo-1:27017', 'isolated-mongo-2:27017'];
        const scenario = process.argv[2];
        const status = {ok: 1, set: 'benchmark', members: [
            {_id: 0, name: hosts[0], health: 1, stateStr: 'PRIMARY'},
            {_id: 1, name: hosts[1], health: 1, stateStr: 'SECONDARY'}
        ], votingMembersCount: 2, writableVotingMembersCount: 2, writeMajorityCount: 2};
        let error = null;
        let stage = 'ReplicaStatus';
        switch (scenario) {
            case 'CommandOk': status.ok = 0; break;
            case 'ReplicaSet': status.set = 'credential-marker'; break;
            case 'MembersShape': status.members = null; break;
            case 'NullMember': status.members[1] = null; break;
            case 'MemberCount': status.members.pop(); break;
            case 'MemberHealth': status.members[1].health = 0; break;
            case 'PrimaryCount': status.members[0].stateStr = 'SECONDARY'; break;
            case 'SecondaryCount': status.members[1].stateStr = 'RECOVERING'; break;
            case 'VotingCount': status.votingMembersCount = 1; break;
            case 'WritableVotingCount': status.writableVotingMembersCount = 1; break;
            case 'WriteMajorityCount': status.writeMajorityCount = 1; break;
            case 'StringCount': status.votingMembersCount = '2'; break;
            case 'ObjectCount': status.votingMembersCount = {$numberLong: '2'}; break;
            case 'AbsentCount': delete status.votingMembersCount; break;
            case 'BooleanCount': status.votingMembersCount = true; break;
            case 'NegativeCount': status.votingMembersCount = -1; break;
            case 'NativeException': error = {code: 94, codeName: 'NotYetInitialized', message: 'credential-marker', stack: 'credential-marker'}; break;
            case 'UnknownException': error = {code: 94, codeName: 'credential-marker', message: 'credential-marker', stack: 'credential-marker'}; break;
            case 'UnsafeCode': error = {code: 'credential-marker', codeName: 'NotYetInitialized', message: 'credential-marker'}; break;
            case 'UnsafeMembers': status.members[1] = {_id: 'credential-marker', name: 'mongodb://user:credential-marker@foreign', health: 'credential-marker', stateStr: 'credential-marker'}; break;
            case 'Oversized': status.set = 'credential-marker'.repeat(8192); status.members.push(...Array(8192).fill({name: 'credential-marker'})); break;
            case 'UnknownStage': stage = 'credential-marker'; break;
        }
        const projection = projectMongoDiagnostic(stage, hosts[0], status, error, hosts, 'benchmark');
        process.stdout.write(JSON.stringify(projection));
        """;

    /// <summary>AC-ISO-003/005/006: invoke only the actual script's pure projection, preserving every original predicate.</summary>
    [Test]
    [Arguments("None", "None")]
    [Arguments("CommandOk", "CommandOk")]
    [Arguments("ReplicaSet", "ReplicaSet")]
    [Arguments("MembersShape", "MembersShape")]
    [Arguments("NullMember", "MembersShape")]
    [Arguments("MemberCount", "MemberCount")]
    [Arguments("MemberHealth", "MemberHealth")]
    [Arguments("PrimaryCount", "PrimaryCount")]
    [Arguments("SecondaryCount", "SecondaryCount")]
    [Arguments("VotingCount", "VotingCount")]
    [Arguments("WritableVotingCount", "WritableVotingCount")]
    [Arguments("WriteMajorityCount", "WriteMajorityCount")]
    [Arguments("NativeException", "NativeException")]
    public async Task ProjectsOriginalReadinessFailure(string scenario, string expected)
    {
        using var projection = JsonDocument.Parse(await MongoDiagnosticProcess.RunAsync(ProjectionHarness, scenario));
        await Assert.That(projection.RootElement.GetProperty(PredicateField).GetString()).IsEqualTo(expected);
        await Assert.That(projection.RootElement.GetProperty(HostField).GetString()).IsEqualTo("isolated-mongo-1:27017");
    }

    [Test]
    [Arguments("StringCount", "string")]
    [Arguments("ObjectCount", "object")]
    [Arguments("AbsentCount", "missing")]
    [Arguments("BooleanCount", "boolean")]
    [Arguments("NegativeCount", "number")]
    public async Task NumericTypesRemainVisibleWithoutCoercingReadiness(string scenario, string expectedKind)
    {
        using var projection = JsonDocument.Parse(await MongoDiagnosticProcess.RunAsync(ProjectionHarness, scenario));
        var count = projection.RootElement.GetProperty(VotingCountField);
        await Assert.That(count.GetProperty(KindField).GetString()).IsEqualTo(expectedKind);
        await Assert.That(count.GetProperty(ValueField).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(projection.RootElement.GetProperty(PredicateField).GetString()).IsEqualTo("VotingCount");
    }

    [Test]
    [Arguments("ReplicaSet")]
    [Arguments("UnsafeMembers")]
    [Arguments("UnknownException")]
    [Arguments("UnsafeCode")]
    [Arguments("Oversized")]
    [Arguments("UnknownStage")]
    public async Task UnknownNativeTextUrisAndOversizedObservationsAreNeverExported(string scenario)
    {
        var text = await MongoDiagnosticProcess.RunAsync(ProjectionHarness, scenario);
        using var projection = JsonDocument.Parse(text);
        await Assert.That(text.Contains("credential-marker", StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains("mongodb://", StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Length <= MongoDiagnosticProcess.MaximumCharacters).IsTrue();
        await Assert.That(projection.RootElement.GetProperty(MembersField).GetArrayLength() <= 3).IsTrue();
    }

    [Test]
    public async Task NativeNumericCodeHasOnlyItsValidatedPinnedCodeName()
    {
        using var known = JsonDocument.Parse(await MongoDiagnosticProcess.RunAsync(ProjectionHarness, "NativeException"));
        using var unknown = JsonDocument.Parse(await MongoDiagnosticProcess.RunAsync(ProjectionHarness, "UnknownException"));
        await Assert.That(known.RootElement.GetProperty(CodeField).GetInt32()).IsEqualTo(94);
        await Assert.That(known.RootElement.GetProperty(CodeNameField).GetString()).IsEqualTo("NotYetInitialized");
        await Assert.That(unknown.RootElement.GetProperty(CodeField).GetInt32()).IsEqualTo(94);
        await Assert.That(unknown.RootElement.GetProperty(CodeNameField).ValueKind).IsEqualTo(JsonValueKind.Null);
    }
}
