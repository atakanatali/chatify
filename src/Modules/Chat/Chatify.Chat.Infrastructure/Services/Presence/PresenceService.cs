using Chatify.Chat.Application.Common.Constants;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Chatify.Chat.Infrastructure.Services.Presence;

/// <summary>
/// Redis-based implementation of <see cref="IPresenceService"/> for managing
/// user presence information using Redis data structures with automatic expiration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This service manages user online/offline status using Redis
/// sets and sorted sets, enabling presence tracking across multiple pod instances
/// in a distributed deployment.
/// </para>
/// <para>
/// <b>Redis Key Structure:</b>
/// <list type="bullet">
/// <item><c>presence:user:{userId}</c> - Sorted set of (connectionId, podId) pairs with TTL</item>
/// <item><c>route:{userId}:{connectionId}</c> - String storing podId for routing</item>
/// </list>
/// </para>
/// <para>
/// <b>Data Structure Semantics:</b>
/// <list type="bullet">
/// <item><b>Presence Set:</b> Sorted set where score = timestamp, member = "{podId}:{connectionId}"</item>
/// <item><b>TTL:</b> 60 seconds on presence keys, refreshed by heartbeat and any activity</item>
/// <item><b>Routing:</b> Each connection has a dedicated key for O(1) pod lookup</item>
/// </list>
/// </para>
/// <para>
/// <b>Multi-Pod Distribution:</b> Using Redis ensures all pods see the same presence state,
/// enabling proper routing of messages to users regardless of which pod they're connected to.
/// </para>
/// <para>
/// <b>Expiration Strategy:</b> Presence records have automatic TTL-based expiration to handle
/// cases where users disconnect without proper sign-off. The heartbeat method refreshes the TTL
/// to keep active connections alive.
/// </para>
/// <para>
/// <b>Connection Identity:</b> Each connection is uniquely identified by the combination of
/// (podId, connectionId). This allows a single user to have multiple connections across
/// different pods or on the same pod.
/// </para>
/// </remarks>
public sealed class PresenceService : IPresenceService
{
    /// <summary>
    /// The Redis key prefix for user presence sets.
    /// </summary>
    /// <remarks>
    /// Full key format: <c>presence:user:{userId}</c>
    /// </remarks>
    private const string PresenceKeyPrefix = "presence:user:";

    /// <summary>
    /// The Redis key prefix for routing information.
    /// </summary>
    /// <remarks>
    /// Full key format: <c>route:{userId}:{connectionId}</c>
    /// </remarks>
    private const string RouteKeyPrefix = "route:";

    /// <summary>
    /// The time-to-live for presence keys in seconds.
    /// </summary>
    /// <remarks>
    /// If a presence key is not refreshed within this duration, it will be automatically
    /// expired by Redis. This handles cases where connections are dropped without
    /// proper disconnect notification.
    /// </remarks>
    private const int PresenceTtlSeconds = 60;

    /// <summary>
    /// The separator used to combine podId and connectionId in sorted set members.
    /// </summary>
    /// <remarks>
    /// This character is unlikely to appear in pod IDs or connection IDs, making it
    /// a safe delimiter for parsing.
    /// </remarks>
    private const string PodConnectionSeparator = ":";

    /// <summary>
    /// Gets the Redis connection multiplexer for database operations.
    /// </summary>
    /// <remarks>
    /// The multiplexer provides thread-safe access to Redis connections and is
    /// typically registered as a singleton in the DI container.
    /// </remarks>
    private readonly IConnectionMultiplexer _redis;

    /// <summary>
    /// Gets the distributed store configuration options.
    /// </summary>
    /// <remarks>
    /// Contains the connection string and store-specific settings.
    /// </remarks>
    private readonly RedisOptionsEntity _options;

    /// <summary>
    /// Gets the logger instance for diagnostic information.
    /// </summary>
    private readonly ILogger<PresenceService> _logger;

    /// <summary>
    /// Gets the pod identity service for identifying the current pod instance.
    /// </summary>
    /// <remarks>
    /// This is used to stamp connections with the pod ID that handles them,
    /// enabling proper routing in multi-pod deployments.
    /// </remarks>
    private readonly IPodIdentityService _podIdentityService;

    /// <summary>
    /// Initializes a new instance of the <see cref="PresenceService"/> class.
    /// </summary>
    /// <param name="redis">
    /// The Redis connection multiplexer for database operations. Must not be null.
    /// </param>
    /// <param name="options">
    /// The distributed store configuration options. Must not be null.
    /// </param>
    /// <param name="podIdentityService">
    /// The pod identity service for identifying the current pod. Must not be null.
    /// </param>
    /// <param name="logger">
    /// The logger instance for diagnostic information. Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any required parameter is null.
    /// </exception>
    /// <remarks>
    /// The constructor validates dependencies and logs initialization.
    /// </remarks>
    public PresenceService(
        IConnectionMultiplexer redis,
        RedisOptionsEntity options,
        IPodIdentityService podIdentityService,
        ILogger<PresenceService> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _podIdentityService = podIdentityService ?? throw new ArgumentNullException(nameof(podIdentityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation(
            "PresenceService initialized. PodId: {PodId}",
            _podIdentityService.PodId);
    }

    /// <summary>
    /// Marks a user as online and registers their connection asynchronously.
    /// </summary>
    /// <param name="userId">
    /// The unique identifier of the user to mark as online.
    /// Must not be null or empty.
    /// </param>
    /// <param name="connectionId">
    /// The unique identifier for this specific connection. A user may have
    /// multiple simultaneous connections (e.g., desktop + mobile).
    /// Must not be null or empty.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation.
    /// The task completes when the presence record has been updated.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="userId"/> or <paramref name="connectionId"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="userId"/> or <paramref name="connectionId"/>
    /// is empty or whitespace-only.
    /// </exception>
    /// <exception cref="RedisException">
    /// Thrown when a Redis operation fails.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Redis Operations:</b> This method executes:
    /// <list type="number">
    /// <item>ZADD to presence set with current timestamp as score</item>
    /// <item>SETEX for routing key with TTL</item>
    /// <item>EXPIRE on presence set to refresh TTL</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Multiple Connections:</b> A single user may have multiple active
    /// connections (e.g., browser tab on desktop, mobile app). Each connection
    /// should register separately with a unique connectionId. The user is
    /// considered online as long as at least one connection is active.
    /// </para>
    /// <para>
    /// <b>Expiry:</b> Presence records have an automatic expiration of
    /// <c><see cref="PresenceTtlSeconds"/></c> seconds to handle network failures.
    /// The connection should send periodic heartbeats to refresh the expiration
    /// and remain marked as online.
    /// </para>
    /// </remarks>
    public Task SetOnlineAsync(
        string userId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));
        }
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            throw new ArgumentException("Connection ID cannot be null or empty.", nameof(connectionId));
        }

        var db = _redis.GetDatabase();
        var podId = _podIdentityService.PodId;
        var presenceKey = GetPresenceKey(userId);
        var routeKey = GetRouteKey(userId, connectionId);
        var member = GetMember(podId, connectionId);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Create a batch for atomic operations
        var batch = db.CreateBatch();

        // Add to presence sorted set with timestamp as score
        batch.SortedSetAddAsync(
            presenceKey,
            member,
            timestamp,
            When.Always,
            CommandFlags.FireAndForget);

        // Set routing key for pod lookup
        batch.StringSetAsync(
            routeKey,
            podId,
            expiry: TimeSpan.FromSeconds(PresenceTtlSeconds),
            When.Always,
            CommandFlags.FireAndForget);

        // Refresh TTL on presence key
        batch.KeyExpireAsync(
            presenceKey,
            expiry: TimeSpan.FromSeconds(PresenceTtlSeconds),
            CommandFlags.FireAndForget);

        batch.Execute();

        _logger.LogDebug(
            "User {UserId} is online. ConnectionId: {ConnectionId}, PodId: {PodId}",
            userId,
            connectionId,
            podId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Marks a user as offline by removing a specific connection asynchronously.
    /// </summary>
    /// <param name="userId">
    /// The unique identifier of the user to update.
    /// Must not be null or empty.
    /// </param>
    /// <param name="connectionId">
    /// The unique identifier for the connection to remove.
    /// Must not be null or empty.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation.
    /// The task completes when the presence record has been updated.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="userId"/> or <paramref name="connectionId"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="userId"/> or <paramref name="connectionId"/>
    /// is empty or whitespace-only.
    /// </exception>
    /// <exception cref="RedisException">
    /// Thrown when a Redis operation fails.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Redis Operations:</b> This method executes:
    /// <list type="number">
    /// <item>ZREM from presence set to remove the specific connection</item>
    /// <item>DEL on routing key</item>
    /// <item>Check if presence set is empty and delete if so</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Last Connection:</b> When a user's last active connection is removed,
    /// the user should be marked as offline overall. This status change can be
    /// broadcast to other users to update UI indicators.
    /// </para>
    /// <para>
    /// <b>Graceful Shutdown:</b> This method should be called when a connection
    /// closes gracefully (e.g., user clicks logout, browser sends disconnect
    /// message). For abrupt disconnections, rely on automatic expiration.
    /// </para>
    /// </remarks>
    public async Task SetOfflineAsync(
        string userId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));
        }
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            throw new ArgumentException("Connection ID cannot be null or empty.", nameof(connectionId));
        }

        var db = _redis.GetDatabase();
        var podId = _podIdentityService.PodId;
        var presenceKey = GetPresenceKey(userId);
        var routeKey = GetRouteKey(userId, connectionId);
        var member = GetMember(podId, connectionId);

        // Remove the specific connection from presence set
        var removed = await db.SortedSetRemoveAsync(
            presenceKey,
            member,
            CommandFlags.FireAndForget);

        // Delete the routing key
        await db.KeyDeleteAsync(routeKey, CommandFlags.FireAndForget);

        // Check if user still has other connections
        var remainingCount = await db.SortedSetLengthAsync(presenceKey);

        _logger.LogDebug(
            "User {UserId} connection removed. ConnectionId: {ConnectionId}, PodId: {PodId}, Remaining connections: {RemainingCount}",
            userId,
            connectionId,
            podId,
            remainingCount);

        // If no connections remain, the presence key will be cleaned up by TTL
        // We explicitly delete it here for immediate cleanup
        if (remainingCount == 0)
        {
            await db.KeyDeleteAsync(presenceKey, CommandFlags.FireAndForget);
            _logger.LogInformation(
                "User {UserId} is now offline (no remaining connections)",
                userId);
        }
    }

    /// <summary>
    /// Refreshes the expiration timer for a user's connection asynchronously.
    /// </summary>
    /// <param name="userId">
    /// The unique identifier of the user whose connection is being refreshed.
    /// Must not be null or empty.
    /// </param>
    /// <param name="connectionId">
    /// The unique identifier for the connection to refresh.
    /// Must not be null or empty.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation.
    /// The task completes when the presence record has been updated.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="userId"/> or <paramref name="connectionId"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="userId"/> or <paramref name="connectionId"/>
    /// is empty or whitespace-only.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the presence service is not available or the connection
    /// does not exist.
    /// </exception>
    /// <exception cref="RedisException">
    /// Thrown when a Redis operation fails.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Redis Operations:</b> This method executes:
    /// <list type="number">
    /// <item>ZADD to update the timestamp score for the connection</item>
    /// <item>SETEX to refresh routing key TTL</item>
    /// <item>EXPIRE on presence set to refresh TTL</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Purpose:</b> Heartbeats prevent presence records from expiring due
    /// to inactivity. Active connections should send heartbeats at intervals
    /// shorter than the presence expiration timeout (e.g., heartbeat every
    /// 15 seconds for a 60-second expiration).
    /// </para>
    /// <para>
    /// <b>Implementation:</b> This updates the timestamp score for the connection
    /// member and refreshes the TTL on both the presence set and routing key.
    /// </para>
    /// </remarks>
    public Task HeartbeatAsync(
        string userId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));
        }
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            throw new ArgumentException("Connection ID cannot be null or empty.", nameof(connectionId));
        }

        var db = _redis.GetDatabase();
        var podId = _podIdentityService.PodId;
        var presenceKey = GetPresenceKey(userId);
        var routeKey = GetRouteKey(userId, connectionId);
        var member = GetMember(podId, connectionId);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Create a batch for atomic operations
        var batch = db.CreateBatch();

        // Update timestamp for this connection
        batch.SortedSetAddAsync(
            presenceKey,
            member,
            timestamp,
            When.Always,
            CommandFlags.FireAndForget);

        // Refresh routing key TTL
        batch.StringSetAsync(
            routeKey,
            podId,
            expiry: TimeSpan.FromSeconds(PresenceTtlSeconds),
            When.Always,
            CommandFlags.FireAndForget);

        // Refresh presence set TTL
        batch.KeyExpireAsync(
            presenceKey,
            expiry: TimeSpan.FromSeconds(PresenceTtlSeconds),
            CommandFlags.FireAndForget);

        batch.Execute();

        _logger.LogTrace(
            "Heartbeat processed for user {UserId}, connection {ConnectionId}, pod {PodId}",
            userId,
            connectionId,
            podId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves all active connection IDs for a specific user asynchronously.
    /// </summary>
    /// <param name="userId">
    /// The unique identifier of the user to query.
    /// Must not be null or empty.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A read-only list of connection IDs representing all active connections
    /// for the specified user. Returns an empty list if the user has no active
    /// connections or is not found.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="userId"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="userId"/> is empty or whitespace-only.
    /// </exception>
    /// <exception cref="RedisException">
    /// Thrown when a Redis operation fails.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Redis Operations:</b> This method executes:
    /// <list type="number">
    /// <item>ZRANGE to retrieve all members of the presence set</item>
    /// <item>Parse each member to extract connection IDs</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>User Status:</b> An empty result list indicates the user is offline.
    /// A non-empty list indicates the user is online, with the count representing
    /// how many active connections they have.
    /// </para>
    /// <para>
    /// <b>Message Routing:</b> The returned connection IDs can be used to route
    /// messages to specific connections (e.g., with SignalR's connection-specific
    /// messaging features). Combined with the routing keys, you can determine
    /// which pod each connection is on.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<string>> GetConnectionsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));
        }

        var db = _redis.GetDatabase();
        var presenceKey = GetPresenceKey(userId);

        // Get all members (sorted by timestamp/score)
        var members = await db.SortedSetRangeByRankAsync(
            presenceKey,
            order: Order.Ascending,
            start: 0,
            stop: -1,
            CommandFlags.None);

        // Parse connection IDs from members
        var connectionIds = new List<string>();
        foreach (var member in members)
        {
            var parsed = ParseMember(member);
            if (parsed is not null)
            {
                connectionIds.Add(parsed.ConnectionId);
            }
        }

        _logger.LogDebug(
            "Retrieved {Count} connections for user {UserId}",
            connectionIds.Count,
            userId);

        return connectionIds.AsReadOnly();
    }

    /// <summary>
    /// Gets the Redis key for a user's presence set.
    /// </summary>
    /// <param name="userId">
    /// The user identifier.
    /// </param>
    /// <returns>
    /// The Redis key for the user's presence set.
    /// </returns>
    /// <remarks>
    /// Format: <c>presence:user:{userId}</c>
    /// </remarks>
    private static string GetPresenceKey(string userId)
    {
        return $"{PresenceKeyPrefix}{userId}";
    }

    /// <summary>
    /// Gets the Redis key for routing information.
    /// </summary>
    /// <param name="userId">
    /// The user identifier.
    /// </param>
    /// <param name="connectionId">
    /// The connection identifier.
    /// </param>
    /// <returns>
    /// The Redis key for routing information.
    /// </returns>
    /// <remarks>
    /// Format: <c>route:{userId}:{connectionId}</c>
    /// </remarks>
    private static string GetRouteKey(string userId, string connectionId)
    {
        return $"{RouteKeyPrefix}{userId}{PodConnectionSeparator}{connectionId}";
    }

    /// <summary>
    /// Creates a member string for the sorted set from pod and connection IDs.
    /// </summary>
    /// <param name="podId">
    /// The pod identifier.
    /// </param>
    /// <param name="connectionId">
    /// The connection identifier.
    /// </param>
    /// <returns>
    /// A combined member string: <c>{podId}:{connectionId}</c>
    /// </returns>
    /// <remarks>
    /// This format allows us to store both pod ID and connection ID in a single
    /// sorted set member, enabling efficient queries and routing.
    /// </remarks>
    private static string GetMember(string podId, string connectionId)
    {
        return $"{podId}{PodConnectionSeparator}{connectionId}";
    }

    /// <summary>
    /// Parses a member string into pod and connection IDs.
    /// </summary>
    /// <param name="member">
    /// The member string to parse.
    /// </param>
    /// <returns>
    /// A tuple containing (podId, connectionId), or <c>null</c> if parsing fails.
    /// </returns>
    /// <remarks>
    /// This is the inverse of <see cref="GetMember"/>.
    /// </remarks>
    private static (string PodId, string ConnectionId)? ParseMember(RedisValue member)
    {
        if (member.IsNullOrEmpty)
        {
            return null;
        }

        var memberStr = member.ToString();
        var parts = memberStr.Split(new[] { PodConnectionSeparator }, StringSplitOptions.None);

        if (parts.Length != 2)
        {
            return null;
        }

        return (parts[0], parts[1]);
    }
}
