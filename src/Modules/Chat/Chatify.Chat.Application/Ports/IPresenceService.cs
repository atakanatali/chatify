namespace Chatify.Chat.Application.Ports;

/// <summary>
/// Defines a contract for managing user presence information within the Chatify system.
/// This port represents the presence tracking interface, abstracting the details of
/// real-time status implementations.
/// </summary>
/// <remarks>
/// <para>
/// <b>Port Role:</b> This is a secondary adapter port in Clean Architecture terms.
/// The application layer depends on this abstraction, while the infrastructure
/// layer provides concrete implementations (e.g., Redis with pub/sub, in-memory
/// stores, specialized presence services).
/// </para>
/// <para>
/// <b>Purpose:</b> Presence tracking enables features such as:
/// <list type="bullet">
/// <item>Displaying online/offline status for users</item>
/// <item>Counting active users in a channel</item>
/// <item>Routing messages only to active connections</item>
/// <item>Implementing typing indicators and read receipts</item>
/// </list>
/// </para>
/// <para>
/// <b>Expiry Behavior:</b> Presence records should have automatic expiration
/// to handle cases where users disconnect without proper sign-off (e.g., network
/// failure, browser close). The heartbeat method refreshes the expiration.
/// </para>
/// <para>
/// <b>Distribution:</b> In multi-pod deployments, presence data must be shared
/// across all instances. Implementations typically use a distributed cache
/// like Redis to ensure all pods see the same presence state.
/// </para>
/// </remarks>
public interface IPresenceService
{
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
    /// <exception cref="InvalidOperationException">
    /// Thrown when the presence service is not available.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Multiple Connections:</b> A single user may have multiple active
    /// connections (e.g., browser tab on desktop, mobile app). Each connection
    /// should register separately with a unique connectionId. The user is
    /// considered online as long as at least one connection is active.
    /// </para>
    /// <para>
    /// <b>Expiry:</b> Presence records should have an automatic expiration
    /// (typically 30-60 seconds) to handle network failures. The connection
    /// should send periodic heartbeats to refresh the expiration and remain
    /// marked as online.
    /// </para>
    /// <para>
    /// <b>Usage Pattern:</b>
    /// <code><![CDATA[
    /// // When a SignalR connection connects
    /// await _presenceService.SetOnlineAsync(userId, connectionId, ct);
    /// _logger.LogInformation("User {UserId} is online with connection {ConnectionId}",
    ///     userId, connectionId);
    /// ]]></code>
    /// </para>
    /// </remarks>
    Task SetOnlineAsync(string userId, string connectionId, CancellationToken cancellationToken);

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
    /// <exception cref="InvalidOperationException">
    /// Thrown when the presence service is not available.
    /// </exception>
    /// <remarks>
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
    /// <para>
    /// <b>Usage Pattern:</b>
    /// <code><![CDATA[
    /// // When a SignalR connection disconnects
    /// await _presenceService.SetOfflineAsync(userId, connectionId, ct);
    /// var remaining = await _presenceService.GetConnectionsAsync(userId, ct);
    /// if (remaining.Count == 0)
    /// {
    ///     // Notify others that user went offline
    ///     await _broadcastService.SendUserOffline(userId);
    /// }
    /// ]]></code>
    /// </para>
    /// </remarks>
    Task SetOfflineAsync(string userId, string connectionId, CancellationToken cancellationToken);

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
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> Heartbeats prevent presence records from expiring due
    /// to inactivity. Active connections should send heartbeats at intervals
    /// shorter than the presence expiration timeout (e.g., heartbeat every
    /// 15 seconds for a 60-second expiration).
    /// </para>
    /// <para>
    /// <b>Implementation:</b> In Redis-based implementations, this typically
    /// updates the TTL (time-to-live) on the presence key to extend its lifetime.
    /// In-memory implementations might update a timestamp.
    /// </para>
    /// <para>
    /// <b>Usage Pattern:</b>
    /// <code><![CDATA[
    /// // Called periodically (e.g., every 15 seconds) from client or middleware
    /// await _presenceService.HeartbeatAsync(userId, connectionId, ct);
    /// ]]></code>
    /// </para>
    /// </remarks>
    Task HeartbeatAsync(string userId, string connectionId, CancellationToken cancellationToken);

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
    /// <exception cref="InvalidOperationException">
    /// Thrown when the presence service is not available.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>User Status:</b> An empty result list indicates the user is offline.
    /// A non-empty list indicates the user is online, with the count representing
    /// how many active connections they have.
    /// </para>
    /// <para>
    /// <b>Message Routing:</b> The returned connection IDs can be used to route
    /// messages to specific connections (e.g., with SignalR's connection-specific
    /// messaging features).
    /// </para>
    /// <para>
    /// <b>Usage Pattern:</b>
    /// <code><![CDATA[
    /// var connections = await _presenceService.GetConnectionsAsync(userId, ct);
    /// foreach (var connectionId in connections)
    /// {
    ///     await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveMessage", message);
    /// }
    /// ]]></code>
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<string>> GetConnectionsAsync(string userId, CancellationToken cancellationToken);
}
