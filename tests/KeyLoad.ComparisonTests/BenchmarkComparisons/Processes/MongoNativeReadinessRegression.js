// Actual same-image mongosh globals and authenticated native commands; no substitute responses.
const MongoReadinessFixture = Object.freeze({
    helper: '/bootstrap/isolated-mongo-readiness.js', failure: 'MongoNativeReadinessRegressionFailed',
    domainPassed: 'MongoNativeReadinessDomainPassed', lowerObserved: 'MongoNativeReadinessLowerPrimaryObserved',
    electionPassed: 'MongoNativeReadinessElectionPassed', admissionPassed: 'MongoNativeReadinessAdmissionPassed', set: 'benchmark', database: 'admin',
    scheme: 'mongodb://', direct: '/admin?directConnection=true&serverSelectionTimeoutMS=2000&connectTimeoutMS=2000&socketTimeoutMS=2000',
    deadlineMs: 120000, parentMs: 300000, commandMs: 2000, pollMs: 500, stepDownSeconds: 20,
    first: 0, one: 1, three: 3
});
const MongoReadinessFixtureHosts = Object.freeze(['isolated-mongo-1:27017', 'isolated-mongo-2:27017', 'isolated-mongo-3:27017']);
const MongoReadinessFixtureClients = new Map();
const MongoReadinessFixtureParent = Date.now() + MongoReadinessFixture.parentMs;

function requireMongoFixture(condition) {
    if (!condition) throw new Error(MongoReadinessFixture.failure);
}

function verifyNativeLongDomain() {
    const integer = KeyLoadMongoReadiness.integer;
    const first = integer(Long.fromString('9007199254740992'), Long.ZERO, false);
    const adjacent = integer(Long.fromString('9007199254740993'), Long.ZERO, false);
    requireMongoFixture(first !== null && adjacent !== null && !Long.prototype.equals.call(first, adjacent));
    requireMongoFixture(Long.prototype.equals.call(first, integer(Long.fromString('9007199254740992'), Long.ZERO, false)));
    requireMongoFixture(integer(Long.MAX_VALUE, Long.ZERO, false) !== null);
    requireMongoFixture(integer(Long.MIN_VALUE, Long.MIN_VALUE, false) !== null);
    requireMongoFixture(integer(Long.NEG_ONE, Long.ZERO, false) === null);
    requireMongoFixture(integer(Long.ZERO, Long.ONE, false) === null);
    requireMongoFixture(Long.prototype.equals.call(integer(1, Long.ONE, true), integer(Long.ONE, Long.ONE, false)));
    requireMongoFixture(integer(-1, Long.NEG_ONE, true) !== null);
    requireMongoFixture(integer(-2147483648, Long.MIN_VALUE, true) !== null);
    requireMongoFixture(integer(2147483647, Long.ZERO, true) !== null);
    const invalid = [undefined, null, '1', { _bsontype: 'Long', low: 1, high: 0, unsigned: false },
        Long.fromBits(1, 0, true), Timestamp.fromBits(1, 0), NaN, Infinity, 1.5, 2147483648, -2147483649];
    for (const value of invalid) requireMongoFixture(integer(value, Long.MIN_VALUE, true) === null);
    requireMongoFixture(integer(1, Long.ZERO, false) === null);
    const corrupt = Long.fromBits(1, 0, false);
    corrupt.low = 1.5;
    requireMongoFixture(integer(corrupt, Long.ZERO, false) === null);
    print(MongoReadinessFixture.domainPassed);
}

function fixtureCommandTime() {
    const remaining = MongoReadinessFixtureParent - Date.now();
    requireMongoFixture(remaining > MongoReadinessFixture.first);
    return Math.min(MongoReadinessFixture.commandMs, remaining);
}

async function fixtureAdmin(host) {
    fixtureCommandTime();
    let database = MongoReadinessFixtureClients.get(host);
    if (!database) {
        const connection = await new Mongo(MongoReadinessFixture.scheme + host + MongoReadinessFixture.direct);
        database = connection.getDB(MongoReadinessFixture.database);
        MongoReadinessFixtureClients.set(host, database);
    }
    fixtureCommandTime();
    const authenticated = await database.auth(process.env.MONGO_INITDB_ROOT_USERNAME, process.env.MONGO_INITDB_ROOT_PASSWORD);
    requireMongoFixture(authenticated.ok === MongoReadinessFixture.one);
    return database;
}

async function observeLowerPrimary(initial) {
    while (Date.now() < MongoReadinessFixtureParent) {
        for (const host of MongoReadinessFixtureHosts.slice(MongoReadinessFixture.one)) {
            const database = await fixtureAdmin(host);
            const hello = await database.runCommand({ hello: MongoReadinessFixture.one, maxTimeMS: fixtureCommandTime() });
            if (hello.isWritablePrimary !== true) continue;
            const status = await database.runCommand({ replSetGetStatus: MongoReadinessFixture.one, maxTimeMS: fixtureCommandTime() });
            const term = KeyLoadMongoReadiness.integer(status.term, Long.ZERO, false);
            requireMongoFixture(hello.ok === MongoReadinessFixture.one && hello.me === host && hello.primary === host
                && hello.setName === MongoReadinessFixture.set && term !== null && Long.prototype.greaterThan.call(term, initial.term));
            const candidate = await KeyLoadMongoReadiness.probeRound(MongoReadinessFixtureHosts, MongoReadinessFixture.set,
                Date.now() + MongoReadinessFixture.deadlineMs, fixtureAdmin);
            requireMongoFixture(candidate === null);
            print(MongoReadinessFixture.lowerObserved);
            return;
        }
        await sleep(Math.min(MongoReadinessFixture.pollMs, fixtureCommandTime()));
    }
    throw new Error(MongoReadinessFixture.failure);
}

async function verifyNativeElection() {
    const initial = await KeyLoadMongoReadiness.waitReady(MongoReadinessFixtureHosts, MongoReadinessFixture.set,
        Date.now() + MongoReadinessFixture.deadlineMs, fixtureAdmin);
    requireMongoFixture(initial.members.length === MongoReadinessFixture.three);
    const intended = await fixtureAdmin(MongoReadinessFixtureHosts[MongoReadinessFixture.first]);
    const stepped = await intended.runCommand({ replSetStepDown: MongoReadinessFixture.stepDownSeconds,
        force: true, maxTimeMS: fixtureCommandTime() });
    requireMongoFixture(stepped.ok === MongoReadinessFixture.one);
    await observeLowerPrimary(initial);
    const returned = await KeyLoadMongoReadiness.waitReady(MongoReadinessFixtureHosts, MongoReadinessFixture.set,
        Date.now() + MongoReadinessFixture.deadlineMs, fixtureAdmin);
    requireMongoFixture(returned.primary === MongoReadinessFixtureHosts[MongoReadinessFixture.first]
        && Long.prototype.greaterThan.call(returned.term, initial.term));
    print(MongoReadinessFixture.electionPassed);
}

async function mongoReadinessFixture() {
    await load(MongoReadinessFixture.helper);
    verifyNativeLongDomain();
    const hosts = process.env.KEYLOAD_MONGO_MEMBERS.split(',');
    requireMongoFixture(hosts.length >= MongoReadinessFixture.one && hosts.length <= MongoReadinessFixture.three
        && hosts.every((host, index) => host === MongoReadinessFixtureHosts[index]));
    if (hosts.length === MongoReadinessFixture.three) await verifyNativeElection();
    else await KeyLoadMongoReadiness.waitReady(hosts, MongoReadinessFixture.set,
        Date.now() + MongoReadinessFixture.deadlineMs, fixtureAdmin);
    print(MongoReadinessFixture.admissionPassed);
}

mongoReadinessFixture().then(() => quit(0), () => { print(MongoReadinessFixture.failure); quit(1); });
