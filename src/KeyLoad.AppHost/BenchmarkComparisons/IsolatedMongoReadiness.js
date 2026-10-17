// Loaded only by same-image native mongosh composition and its genuine native regression.
const MongoReadiness = Object.freeze({
    zero: 0, one: 1, two: 2, maximumNodes: 3, commandMs: 2000, pollMs: 500,
    int32Minimum: -2147483648, int32Maximum: 2147483647,
    number: 'number', primary: 'PRIMARY', secondary: 'SECONDARY', set: 'benchmark',
    timeout: 'MongoNativeBootstrapTimeout', invalid: 'MongoNativeReadinessInvalid'
});
const MongoReadinessHosts = Object.freeze(['isolated-mongo-1:27017', 'isolated-mongo-2:27017', 'isolated-mongo-3:27017']);

function mongoReadinessInt32(value) {
    return typeof value === MongoReadiness.number && Number.isInteger(value)
        && value >= MongoReadiness.int32Minimum && value <= MongoReadiness.int32Maximum;
}

function mongoReadinessNative(value) {
    return value instanceof Long && Object.getPrototypeOf(value) === Long.prototype && value.unsigned === false
        && mongoReadinessInt32(value.low) && mongoReadinessInt32(value.high);
}

function mongoReadinessInteger(value, minimum, allowInt32) {
    if (!mongoReadinessNative(minimum)) return null;
    let copied;
    if (mongoReadinessNative(value)) copied = Long.fromBits(value.low, value.high, false);
    else if (allowInt32 && mongoReadinessInt32(value)) copied = Long.fromInt(value, false);
    else return null;
    return Long.prototype.lessThan.call(copied, minimum) ? null : copied;
}

function mongoReadinessEqual(left, right) {
    return mongoReadinessNative(left) && mongoReadinessNative(right) && Long.prototype.equals.call(left, right);
}

function mongoReadinessTime(deadline) {
    const remaining = deadline - Date.now();
    if (!Number.isSafeInteger(remaining) || remaining <= MongoReadiness.zero) throw new Error(MongoReadiness.timeout);
    return Math.min(MongoReadiness.commandMs, remaining);
}

function mongoReadinessSelection(hosts, set) {
    if (!Array.isArray(hosts) || hosts.length < MongoReadiness.one || hosts.length > MongoReadiness.maximumNodes
        || set !== MongoReadiness.set || !hosts.every((host, index) => host === MongoReadinessHosts[index])) {
        throw new Error(MongoReadiness.invalid);
    }
}

function mongoReadinessConfiguration(config, hosts, term) {
    const version = mongoReadinessInteger(config?.version, Long.ONE, true);
    const configTerm = mongoReadinessInteger(config?.term, Long.NEG_ONE, true);
    if (config?._id !== MongoReadiness.set || version === null || configTerm === null
        || Long.prototype.greaterThan.call(configTerm, term) || !Array.isArray(config.members)
        || config.members.length !== hosts.length) return null;
    for (let index = MongoReadiness.zero; index < hosts.length; index++) {
        const matches = config.members.filter(member => member?._id === index && member.host === hosts[index]);
        if (matches.length !== MongoReadiness.one) return null;
        const member = matches[MongoReadiness.zero];
        const delay = member.secondaryDelaySecs === undefined ? Long.ZERO : mongoReadinessInteger(member.secondaryDelaySecs, Long.ZERO, false);
        if (member.votes !== MongoReadiness.one || member.priority !== (index === MongoReadiness.zero ? MongoReadiness.two : MongoReadiness.one)
            || member.arbiterOnly !== false || member.hidden !== false || !mongoReadinessEqual(delay, Long.ZERO)) return null;
    }
    return { version, configTerm };
}

function mongoReadinessMembers(status, hosts, queried, version, configTerm) {
    if (!Array.isArray(status.members) || status.members.length !== hosts.length
        || status.members.filter(member => member?.self === true).length !== MongoReadiness.one) return null;
    const members = [];
    for (let index = MongoReadiness.zero; index < hosts.length; index++) {
        const matches = status.members.filter(member => member?._id === index && member.name === hosts[index]);
        if (matches.length !== MongoReadiness.one) return null;
        const member = matches[MongoReadiness.zero];
        const state = index === MongoReadiness.zero ? MongoReadiness.primary : MongoReadiness.secondary;
        const stateNumber = index === MongoReadiness.zero ? MongoReadiness.one : MongoReadiness.two;
        const memberVersion = mongoReadinessInteger(member.configVersion, Long.ONE, true);
        const memberTerm = mongoReadinessInteger(member.configTerm, Long.NEG_ONE, true);
        if (member.health !== MongoReadiness.one || member.stateStr !== state || member.state !== stateNumber
            || (member.self === true) !== (member.name === queried)
            || !mongoReadinessEqual(memberVersion, version) || !mongoReadinessEqual(memberTerm, configTerm)) return null;
        members.push({ id: index, host: member.name, state, health: member.health, version: memberVersion, configTerm: memberTerm });
    }
    return members;
}

function mongoReadinessHello(hello, hosts, queried, version) {
    return hello.ok === MongoReadiness.one && hello.setName === MongoReadiness.set && hello.me === queried
        && hello.primary === hosts[MongoReadiness.zero] && hello.isWritablePrimary === (queried === hosts[MongoReadiness.zero])
        && Array.isArray(hello.hosts) && hello.hosts.length === hosts.length
        && hosts.every(host => hello.hosts.filter(value => value === host).length === MongoReadiness.one)
        && (hello.passives === undefined || Array.isArray(hello.passives) && hello.passives.length === MongoReadiness.zero)
        && (hello.arbiters === undefined || Array.isArray(hello.arbiters) && hello.arbiters.length === MongoReadiness.zero)
        && mongoReadinessEqual(mongoReadinessInteger(hello.setVersion, Long.ONE, true), version);
}

function mongoReadinessTuple(status, hello, config, hosts, queried) {
    const term = mongoReadinessInteger(status.term, Long.ZERO, false);
    if (status.ok !== MongoReadiness.one || status.set !== MongoReadiness.set || term === null) return null;
    const configured = mongoReadinessConfiguration(config, hosts, term);
    if (configured === null || !mongoReadinessHello(hello, hosts, queried, configured.version)) return null;
    const members = mongoReadinessMembers(status, hosts, queried, configured.version, configured.configTerm);
    const majority = Math.floor(hosts.length / MongoReadiness.two) + MongoReadiness.one;
    if (members === null || status.votingMembersCount !== hosts.length || status.writableVotingMembersCount !== hosts.length
        || status.majorityVoteCount !== majority || status.writeMajorityCount !== majority) return null;
    return { standalone: false, set: status.set, primary: hello.primary, term, version: configured.version,
        configTerm: configured.configTerm, members, votes: status.votingMembersCount, writable: status.writableVotingMembersCount,
        voteMajority: status.majorityVoteCount, writeMajority: status.writeMajorityCount };
}

function mongoReadinessSameRound(left, right) {
    if (left === null || right === null || left.standalone !== right.standalone || left.primary !== right.primary) return false;
    if (left.standalone) return true;
    return left.set === right.set && mongoReadinessEqual(left.term, right.term) && mongoReadinessEqual(left.version, right.version)
        && mongoReadinessEqual(left.configTerm, right.configTerm) && left.votes === right.votes && left.writable === right.writable
        && left.voteMajority === right.voteMajority && left.writeMajority === right.writeMajority
        && left.members.length === right.members.length && left.members.every((member, index) => {
            const other = right.members[index];
            return member.id === other.id && member.host === other.host && member.state === other.state && member.health === other.health
                && mongoReadinessEqual(member.version, other.version) && mongoReadinessEqual(member.configTerm, other.configTerm);
        });
}

async function mongoReadinessStandalone(hosts, deadline, nativeAdmin) {
    const host = hosts[MongoReadiness.zero];
    const ping = await mongoReadinessCommand(host, deadline, nativeAdmin, { ping: MongoReadiness.one });
    const hello = await mongoReadinessCommand(host, deadline, nativeAdmin, { hello: MongoReadiness.one });
    mongoReadinessTime(deadline);
    return ping.ok === MongoReadiness.one && hello.ok === MongoReadiness.one && hello.isWritablePrimary === true
        && hello.setName === undefined && hello.hosts === undefined ? { standalone: true, primary: hosts[MongoReadiness.zero] } : null;
}

async function mongoReadinessCommand(host, deadline, nativeAdmin, command) {
    mongoReadinessTime(deadline);
    const database = await nativeAdmin(host);
    return await database.runCommand({ ...command, maxTimeMS: mongoReadinessTime(deadline) });
}

async function mongoReadinessProbeRound(hosts, set, deadline, nativeAdmin) {
    mongoReadinessSelection(hosts, set);
    if (hosts.length === MongoReadiness.one) return await mongoReadinessStandalone(hosts, deadline, nativeAdmin);
    let result = null;
    for (const host of hosts) {
        const status = await mongoReadinessCommand(host, deadline, nativeAdmin, { replSetGetStatus: MongoReadiness.one });
        const hello = await mongoReadinessCommand(host, deadline, nativeAdmin, { hello: MongoReadiness.one });
        const configured = await mongoReadinessCommand(host, deadline, nativeAdmin, { replSetGetConfig: MongoReadiness.one });
        if (configured.ok !== MongoReadiness.one) return null;
        const current = mongoReadinessTuple(status, hello, configured.config, hosts, host);
        if (current === null || result !== null && !mongoReadinessSameRound(result, current)) return null;
        result = current;
    }
    mongoReadinessTime(deadline);
    return result;
}

async function mongoReadinessWaitReady(hosts, set, deadline, nativeAdmin) {
    let previous = null;
    while (Date.now() < deadline) {
        try {
            const current = await mongoReadinessProbeRound(hosts, set, deadline, nativeAdmin);
            if (current !== null && mongoReadinessSameRound(previous, current)) return current;
            previous = current;
        } catch {
            // Only untimed native admission polling; no exception message or credentials are emitted.
            previous = null;
        }
        const remaining = deadline - Date.now();
        if (remaining <= MongoReadiness.zero) break;
        await sleep(Math.min(MongoReadiness.pollMs, remaining));
    }
    throw new Error(MongoReadiness.timeout);
}

globalThis.KeyLoadMongoReadiness = Object.freeze({ probeRound: mongoReadinessProbeRound,
    waitReady: mongoReadinessWaitReady, sameRound: mongoReadinessSameRound, integer: mongoReadinessInteger });
