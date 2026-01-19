const signalR = require("@microsoft/signalr");
const axios = require("axios");

// Configuration
const HUB_URLS = [
    "http://localhost:3001/hubs/chat",
    "http://localhost:3002/hubs/chat",
    "http://localhost:3003/hubs/chat"
];

// Test Scenarios
const CLIENTS = [
    { name: "A", podIndex: 0, userId: "user-a", scopeId: "scope-1" },
    { name: "B", podIndex: 1, userId: "user-b", scopeId: "scope-1" }, // Pair 1 (Cross-Pod: A->Pod1, B->Pod2)
    { name: "C", podIndex: 1, userId: "user-c", scopeId: "scope-2" },
    { name: "D", podIndex: 1, userId: "user-d", scopeId: "scope-2" }, // Pair 2
    { name: "E", podIndex: 2, userId: "user-e", scopeId: "scope-3" },
    { name: "F", podIndex: 2, userId: "user-f", scopeId: "scope-3" }  // Pair 3
];

const connections = {};

async function createConnection(client) {
    const url = HUB_URLS[client.podIndex];
    console.log(`[${client.name}] Connecting to ${url}...`);

    const connection = new signalR.HubConnectionBuilder()
        .withUrl(url)
        .configureLogging(signalR.LogLevel.Information)
        .build();

    connection.on("ReceiveMessage", (message) => {
        console.log(`✅ [${client.name}] RECEIVED message: ${message.content} (from ${message.senderId})`);
    });

    try {
        await connection.start();
        console.log(`[${client.name}] Connected! ConnectionId: ${connection.connectionId}`);

        // Join Scope (Group)
        await connection.invoke("JoinScopeAsync", client.scopeId);
        console.log(`[${client.name}] Joined Scope: ${client.scopeId}`);

        connections[client.name] = connection;
    } catch (err) {
        console.error(`[${client.name}] Connection failed: ${err}`);
    }
}

async function runTest() {
    console.log("--- Starting E2E Broadcast Test ---");

    // 1. Establish Connections
    await Promise.all(CLIENTS.map(createConnection));

    // 2. Wait for groups to sync
    await new Promise(r => setTimeout(r, 2000));

    // 3. Send Message from A -> B (Scope 1)
    console.log("\n--- Sending Message A -> Scope 1 ---");
    try {
        const sender = connections["A"];
        await sender.invoke("SendAsync", {
            scopeId: "scope-1",
            text: "Hello form A!",
            scopeType: 0
        });
        console.log("[A] Message Sent!");
    } catch (err) {
        console.error("[A] Send Failed:", err);
    }

    // 4. Send Message from C -> E (Cross-Pod? No, scopes are isolated)
    console.log("\n--- Sending Message C -> Scope 2 ---");
    try {
        const sender = connections["C"];
        await sender.invoke("SendAsync", {
            scopeId: "scope-2",
            text: "Hello from C!",
            scopeType: 0
        });
        console.log("[C] Message Sent!");
    } catch (err) {
        console.error("[C] Send Failed:", err);
    }

    // 5. Wait for propagation
    await new Promise(r => setTimeout(r, 5000));

    // 6. Cleanup
    console.log("\n--- Stopping Connections ---");
    for (const name in connections) {
        if (connections[name]) {
            await connections[name].stop();
        }
    }
}

runTest();
