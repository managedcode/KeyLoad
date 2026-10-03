// Runs once as a same-image native mongosh client, outside every timed operation.
const MongoDiagnosticStage = Object.freeze({ authentication: 'Authentication', ping: 'Ping', initiate: 'Initiate', replica: 'ReplicaStatus', unknown: 'Unknown' });
const MongoDiagnosticPredicate = Object.freeze({
    none: 'None', awaiting: 'AwaitingResponse', exception: 'NativeException', command: 'CommandOk', set: 'ReplicaSet', shape: 'MembersShape',
    count: 'MemberCount', health: 'MemberHealth', primary: 'PrimaryCount', secondary: 'SecondaryCount', voting: 'VotingCount',
    writable: 'WritableVotingCount', majority: 'WriteMajorityCount', unknown: 'UnknownStage'
});
const MongoDiagnosticKind = Object.freeze({ missing: 'missing', null: 'null', array: 'array', number: 'number', string: 'string', object: 'object', boolean: 'boolean', other: 'other' });
const MongoDiagnosticLimits = Object.freeze({ members: 3, characters: 4096, integer: 2147483647 });
const MongoDiagnosticSet = 'benchmark';
const MongoDiagnosticPrimary = 'PRIMARY', MongoDiagnosticSecondary = 'SECONDARY';
const MongoDiagnosticHosts = Object.freeze(['isolated-mongo-1:27017', 'isolated-mongo-2:27017', 'isolated-mongo-3:27017']);
const MongoDiagnosticStates = Object.freeze(['STARTUP', MongoDiagnosticPrimary, MongoDiagnosticSecondary, 'RECOVERING', 'STARTUP2', 'UNKNOWN', 'ARBITER', 'DOWN', 'ROLLBACK', 'REMOVED']);
// Closed diagnostic names from the pinned mongo r8.3.9 error_codes.yml; unknown names remain null.
const MongoDiagnosticCodeNames = new Map([
    [6, 'HostUnreachable'], [7, 'HostNotFound'], [13, 'Unauthorized'], [18, 'AuthenticationFailed'], [23, 'AlreadyInitialized'],
    [50, 'MaxTimeMSExpired'], [89, 'NetworkTimeout'], [91, 'ShutdownInProgress'], [93, 'InvalidReplicaSetConfig'], [94, 'NotYetInitialized'],
    [109, 'ConfigurationInProgress'], [10107, 'NotWritablePrimary'], [11600, 'InterruptedAtShutdown'], [11601, 'Interrupted'],
    [11602, 'InterruptedDueToReplStateChange'], [13435, 'NotPrimaryNoSecondaryOk']
]);
const MongoDiagnosticPrefix = 'MongoNativeBootstrapDiagnostic ';

function mongoDiagnosticNumber(value) {
    return typeof value === MongoDiagnosticKind.number && Number.isSafeInteger(value) && value >= 0 && value <= MongoDiagnosticLimits.integer ? value : null;
}

function mongoDiagnosticTypedCount(value) {
    let kind = typeof value;
    if (value === undefined) kind = MongoDiagnosticKind.missing;
    else if (value === null) kind = MongoDiagnosticKind.null;
    else if (Array.isArray(value)) kind = MongoDiagnosticKind.array;
    else if (!Object.values(MongoDiagnosticKind).includes(kind)) kind = MongoDiagnosticKind.other;
    return { kind, value: mongoDiagnosticNumber(value) };
}

function mongoDiagnosticHost(value, expectedHosts) {
    return MongoDiagnosticHosts.includes(value) && expectedHosts.includes(value) ? value : null;
}

function mongoDiagnosticError(error) {
    const code = mongoDiagnosticNumber(error?.code);
    const codeName = code !== null && MongoDiagnosticCodeNames.get(code) === error?.codeName ? error.codeName : null;
    return { code, codeName };
}

function mongoDiagnosticMember(member, expectedHosts) {
    const id = mongoDiagnosticNumber(member?._id);
    return {
        id: id !== null && id < MongoDiagnosticLimits.members ? id : null,
        name: mongoDiagnosticHost(member?.name, expectedHosts),
        health: mongoDiagnosticTypedCount(member?.health),
        state: MongoDiagnosticStates.includes(member?.stateStr) ? member.stateStr : null
    };
}

function mongoDiagnosticPredicate(stage, status, error, expectedHosts, expectedSet) {
    if (!Object.values(MongoDiagnosticStage).includes(stage) || stage === MongoDiagnosticStage.unknown) return MongoDiagnosticPredicate.unknown;
    if (error) return MongoDiagnosticPredicate.exception;
    if (!status) return MongoDiagnosticPredicate.awaiting;
    if (status.ok !== 1) return MongoDiagnosticPredicate.command;
    if (stage !== MongoDiagnosticStage.replica) return MongoDiagnosticPredicate.none;
    if (status.set !== expectedSet) return MongoDiagnosticPredicate.set;
    if (!Array.isArray(status.members)) return MongoDiagnosticPredicate.shape;
    if (status.members.length !== expectedHosts.length) return MongoDiagnosticPredicate.count;
    if (status.members.some(member => member === null || typeof member !== MongoDiagnosticKind.object)) return MongoDiagnosticPredicate.shape;
    if (!status.members.every(member => member.health === 1)) return MongoDiagnosticPredicate.health;
    if (status.members.filter(member => member.stateStr === MongoDiagnosticPrimary).length !== 1) return MongoDiagnosticPredicate.primary;
    if (status.members.filter(member => member.stateStr === MongoDiagnosticSecondary).length !== expectedHosts.length - 1) return MongoDiagnosticPredicate.secondary;
    if (status.votingMembersCount !== expectedHosts.length) return MongoDiagnosticPredicate.voting;
    if (status.writableVotingMembersCount !== expectedHosts.length) return MongoDiagnosticPredicate.writable;
    if (status.writeMajorityCount !== Math.floor(expectedHosts.length / 2) + 1) return MongoDiagnosticPredicate.majority;
    return MongoDiagnosticPredicate.none;
}

function projectMongoDiagnostic(stage, host, status, error, expectedHosts, expectedSet) {
    return {
        stage: Object.values(MongoDiagnosticStage).includes(stage) ? stage : MongoDiagnosticStage.unknown,
        predicate: mongoDiagnosticPredicate(stage, status, error, expectedHosts, expectedSet),
        host: mongoDiagnosticHost(host, expectedHosts),
        set: expectedSet === MongoDiagnosticSet && status?.set === expectedSet ? expectedSet : null,
        ok: mongoDiagnosticTypedCount(status?.ok),
        memberCount: mongoDiagnosticTypedCount(Array.isArray(status?.members) ? status.members.length : undefined),
        members: Array.isArray(status?.members) ? status.members.slice(0, MongoDiagnosticLimits.members).map(member => mongoDiagnosticMember(member, expectedHosts)) : [],
        votingMembersCount: mongoDiagnosticTypedCount(status?.votingMembersCount),
        writableVotingMembersCount: mongoDiagnosticTypedCount(status?.writableVotingMembersCount),
        writeMajorityCount: mongoDiagnosticTypedCount(status?.writeMajorityCount),
        ...mongoDiagnosticError(error ?? status)
    };
}

// Native bootstrap composition.
const hosts = process.env.KEYLOAD_MONGO_MEMBERS.split(',');
const set = process.env.KEYLOAD_MONGO_REPLICA_SET;
const username = process.env.MONGO_INITDB_ROOT_USERNAME;
const password = process.env.MONGO_INITDB_ROOT_PASSWORD;
const deadline = Date.now() + 120000;

const clients = new Map();
let lastDiagnostic = null;

function rememberDiagnostic(stage, host, status) {
    lastDiagnostic = projectMongoDiagnostic(stage, host, status, null, hosts, set);
}

function rememberException(error) {
    if (lastDiagnostic === null) rememberDiagnostic(MongoDiagnosticStage.unknown, null, null);
    lastDiagnostic = { ...lastDiagnostic, predicate: MongoDiagnosticPredicate.exception, ...mongoDiagnosticError(error) };
}

function printDiagnostic() {
    const text = JSON.stringify(lastDiagnostic);
    const bounded = text.length <= MongoDiagnosticLimits.characters ? text : JSON.stringify({ stage: MongoDiagnosticStage.unknown, predicate: MongoDiagnosticPredicate.unknown });
    print(MongoDiagnosticPrefix + bounded);
}

async function admin(host) {
    rememberDiagnostic(MongoDiagnosticStage.authentication, host, null);
    let database = clients.get(host);
    if (!database) {
        const connection = await new Mongo('mongodb://' + host + '/admin?directConnection=true&serverSelectionTimeoutMS=2000&connectTimeoutMS=2000&socketTimeoutMS=2000');
        database = connection.getDB('admin');
        clients.set(host, database);
    }
    const authenticated = await database.auth(username, password);
    rememberDiagnostic(MongoDiagnosticStage.authentication, host, authenticated);
    if (authenticated.ok !== 1) {
        throw new Error('MongoNativeAuthenticationFailed');
    }
    return database;
}

async function waitReady(probe) {
    while (Date.now() < deadline) {
        try {
            if (await probe()) return;
        } catch (error) {
            // Bootstrap readiness retries are bounded and never emit connection credentials.
            rememberException(error);
        }
        await sleep(500);
    }
    throw new Error('MongoNativeBootstrapTimeout');
}

async function pingMembers() {
    for (const host of hosts) {
        rememberDiagnostic(MongoDiagnosticStage.ping, host, null);
        const database = await admin(host);
        rememberDiagnostic(MongoDiagnosticStage.ping, host, null);
        const status = await database.runCommand({ ping: 1 });
        rememberDiagnostic(MongoDiagnosticStage.ping, host, status);
        if (status.ok !== 1) return false;
    }
    return true;
}

async function replicaMembersReady() {
    for (const host of hosts) {
        rememberDiagnostic(MongoDiagnosticStage.replica, host, null);
        const database = await admin(host);
        rememberDiagnostic(MongoDiagnosticStage.replica, host, null);
        const status = await database.runCommand({ replSetGetStatus: 1, maxTimeMS: 2000 });
        rememberDiagnostic(MongoDiagnosticStage.replica, host, status);
        const ready = status.ok === 1 && status.set === set && status.members.length === hosts.length &&
            status.members.every(member => member.health === 1) &&
            status.members.filter(member => member.stateStr === 'PRIMARY').length === 1 &&
            status.members.filter(member => member.stateStr === 'SECONDARY').length === hosts.length - 1 &&
            status.votingMembersCount === hosts.length && status.writableVotingMembersCount === hosts.length &&
            status.writeMajorityCount === Math.floor(hosts.length / 2) + 1;
        if (!ready) return false;
    }
    return true;
}

async function bootstrap() {
    await waitReady(pingMembers);
    if (hosts.length > 1) {
        const primary = await admin(hosts[0]);
        rememberDiagnostic(MongoDiagnosticStage.initiate, hosts[0], null);
        const initiated = await primary.runCommand({ replSetInitiate: {
            _id: set, members: hosts.map((host, index) => ({ _id: index, host, votes: 1, priority: index === 0 ? 2 : 1 }))
        } });
        rememberDiagnostic(MongoDiagnosticStage.initiate, hosts[0], initiated);
        if (initiated.ok !== 1) throw new Error('MongoNativeReplicaInitiationFailed');
        await waitReady(replicaMembersReady);
    }
}

bootstrap().then(
    () => quit(0),
    error => {
        // Native failures are retained as a fixed classification without credential-bearing details.
        if (lastDiagnostic === null || lastDiagnostic.predicate === MongoDiagnosticPredicate.none || lastDiagnostic.predicate === MongoDiagnosticPredicate.awaiting) rememberException(error);
        print('MongoNativeBootstrapFailed');
        printDiagnostic();
        quit(1);
    }
);
