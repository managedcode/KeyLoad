// Runs once as a same-image native mongosh client, outside every timed operation.
const hosts = process.env.KEYLOAD_MONGO_MEMBERS.split(',');
const set = process.env.KEYLOAD_MONGO_REPLICA_SET;
const username = process.env.MONGO_INITDB_ROOT_USERNAME;
const password = process.env.MONGO_INITDB_ROOT_PASSWORD;
const deadline = Date.now() + 120000;

const clients = new Map();

async function admin(host) {
    if (clients.has(host)) return clients.get(host);
    const connection = await new Mongo('mongodb://' + host + '/admin?directConnection=true&serverSelectionTimeoutMS=2000&connectTimeoutMS=2000&socketTimeoutMS=2000');
    const database = connection.getDB('admin');
    if ((await database.auth(username, password)).ok !== 1) {
        throw new Error('MongoNativeAuthenticationFailed');
    }
    clients.set(host, database);
    return database;
}

async function waitReady(probe) {
    while (Date.now() < deadline) {
        try {
            if (await probe()) return;
        } catch {
            // Bootstrap readiness retries are bounded and never emit connection credentials.
        }
        await sleep(500);
    }
    throw new Error('MongoNativeBootstrapTimeout');
}

async function pingMembers() {
    for (const host of hosts) {
        const database = await admin(host);
        if ((await database.runCommand({ ping: 1 })).ok !== 1) return false;
    }
    return true;
}

async function replicaMembersReady() {
    for (const host of hosts) {
        const database = await admin(host);
        const status = await database.runCommand({ replSetGetStatus: 1, maxTimeMS: 2000 });
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
        const initiated = await primary.runCommand({ replSetInitiate: {
            _id: set, members: hosts.map((host, index) => ({ _id: index, host, votes: 1, priority: index === 0 ? 2 : 1 }))
        } });
        if (initiated.ok !== 1) throw new Error('MongoNativeReplicaInitiationFailed');
        await waitReady(replicaMembersReady);
    }
}

try {
    await bootstrap();
} catch {
    // Native failures are retained as a fixed classification without credential-bearing details.
    print('MongoNativeBootstrapFailed');
    quit(1);
}
