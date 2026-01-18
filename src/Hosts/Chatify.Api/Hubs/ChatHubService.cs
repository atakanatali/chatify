using Chatify.Api.Extensions;
using Chatify.Chat.Application.Commands.SendChatMessage;
using Chatify.Chat.Application.Dtos;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Domain;
using Microsoft.AspNetCore.SignalR;

namespace Chatify.Api.Hubs;

/// <summary>
/// SignalR hub for real-time chat messaging in the Chatify system.
/// This hub enables clients to join conversation scopes and send messages
/// with real-time broadcast to other participants.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> The ChatHubService provides a real-time communication channel
/// for chat clients using SignalR. It handles scope management (joining/leaving)
/// and message broadcasting within conversation scopes.
/// </para>
/// <para>
/// <b>SignalR Hub:</b> This class extends <see cref="Hub"/>, which provides
/// the base functionality for real-time communication including connection
/// management, group management, and client invocation.
/// </para>
/// <para>
/// <b>Scope-based Grouping:</b> The hub uses SignalR groups to implement
/// scope-based message routing. Each scope ID corresponds to a SignalR group,
/// ensuring messages are only broadcast to participants in the same scope.
/// </para>
/// <para>
/// <b>Presence Integration:</b> The hub integrates with <see cref="IPresenceService"/>
/// to track user connections across all pods in the deployment. This enables:
/// <list type="bullet">
/// <item>Online/offline status tracking</item>
/// <item>Multi-connection support (same user on multiple devices)</item>
/// <item>Automatic cleanup via TTL on abrupt disconnects</item>
/// </list>
/// </para>
/// <para>
/// <b>Message Flow:</b>
/// <list type="number">
/// <item>Client connects and registers presence via <see cref="OnConnectedAsync"/></item>
/// <item>Client calls <see cref="JoinScopeAsync"/> to join a conversation</item>
/// <item>Client calls <see cref="SendAsync"/> to send a message</item>
/// <item>Hub invokes <see cref="SendChatMessageCommandHandler"/> to process the message</item>
/// <item>Message is produced to Kafka for persistence and fan-out</item>
/// <item>Message is broadcast to all clients in the scope via SignalR</item>
/// <item>Client disconnects and removes presence via <see cref="OnDisconnectedAsync"/></item>
/// </list>
/// </para>
/// <para>
/// <b>Connection Lifecycle:</b> SignalR automatically manages connections.
/// When a client disconnects unexpectedly, they are automatically removed
/// from all groups. The presence service handles connection tracking with
/// TTL-based expiration for graceful handling of network failures.
/// </para>
/// <para>
/// <b>Client Example:</b>
/// <code><![CDATA[
/// // JavaScript/TypeScript client
/// const connection = new HubConnectionBuilder()
///     .withUrl("/hubs/chat")
///     .build();
///
/// await connection.start();
/// await connection.invoke("JoinScopeAsync", "general");
/// await connection.invoke("SendAsync", {
///     scopeType: 0, // Channel
///     scopeId: "general",
///     text: "Hello, world!"
/// });
/// ]]></code>
/// </para>
/// <para>
/// <b>wscat Testing:</b>
/// <code><![CDATA[
/// # Note: wscat doesn't support SignalR protocol directly
/// # Use SignalR client libraries or browser DevTools for testing
/// ]]></code>
/// </para>
/// </remarks>
public sealed class ChatHubService : Hub
{
    /// <summary>
    /// The command handler responsible for processing send chat message commands.
    /// </summary>
    /// <remarks>
    /// This handler is injected via dependency injection and processes the
    /// business logic for sending messages, including validation, rate limiting,
    /// and event production.
    /// </remarks>
    private readonly SendChatMessageCommandHandler _sendChatMessageCommandHandler;

    /// <summary>
    /// The presence service for tracking user connections across pods.
    /// </summary>
    /// <remarks>
    /// This service manages user online/offline status in Redis, enabling
    /// distributed presence tracking across all pod instances.
    /// </remarks>
    private readonly IPresenceService _presenceService;

    /// <summary>
    /// The logger used for diagnostic and audit logging.
    /// </summary>
    /// <remarks>
    /// This logger records hub lifecycle events, client connections, and
    /// message operations for debugging and monitoring.
    /// </remarks>
    private readonly ILogger<ChatHubService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatHubService"/> class.
    /// </summary>
    /// <param name="sendChatMessageCommandHandler">
    /// The command handler for processing send chat message commands.
    /// Must not be null.
    /// </param>
    /// <param name="presenceService">
    /// The presence service for tracking user connections.
    /// Must not be null.
    /// </param>
    /// <param name="logger">
    /// The logger for diagnostic and audit information.
    /// Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any required parameter is null.
    /// </exception>
    /// <remarks>
    /// SignalR hubs are created transiently by the framework. The constructor
    /// receives its dependencies via dependency injection for each hub method
    /// invocation.
    /// </remarks>
    public ChatHubService(
        SendChatMessageCommandHandler sendChatMessageCommandHandler,
        IPresenceService presenceService,
        ILogger<ChatHubService> logger)
    {
        _sendChatMessageCommandHandler = sendChatMessageCommandHandler
            ?? throw new ArgumentNullException(nameof(sendChatMessageCommandHandler));
        _presenceService = presenceService
            ?? throw new ArgumentNullException(nameof(presenceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Adds the current connection to a SignalR group corresponding to the specified scope.
    /// </summary>
    /// <param name="scopeId">
    /// The scope identifier to join. This corresponds to a channel name,
    /// conversation ID, or other scope identifier. Must not be null or empty.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="scopeId"/> is null or empty.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> This method adds the current client connection to a SignalR
    /// group identified by <paramref name="scopeId"/>. Once joined, the client will
    /// receive all messages broadcast to that scope.
    /// </para>
    /// <para>
    /// <b>SignalR Groups:</b> SignalR groups provide a way to broadcast messages
    /// to a subset of connected clients. Each scope ID corresponds to a group,
    /// enabling scope-based message routing.
    /// </para>
    /// <para>
    /// <b>Idempotency:</b> Calling this method multiple times with the same scope
    /// ID is safe. The client will only be added to the group once.
    /// </para>
    /// <para>
    /// <b>Multi-Scope Support:</b> Clients can join multiple scopes simultaneously.
    /// This is useful for users participating in multiple conversations.
    /// </para>
    /// <para>
    /// <b>Client Usage:</b>
    /// <code><![CDATA[
    /// await connection.invoke("JoinScopeAsync", "general");
    /// await connection.invoke("JoinScopeAsync", "random");
    /// // Client now receives messages from both scopes
    /// ]]></code>
    /// </para>
    /// </remarks>
    public async Task JoinScopeAsync(string scopeId)
    {
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            throw new ArgumentNullException(nameof(scopeId));
        }

        var connectionId = Context.ConnectionId;
        _logger.LogDebug("Client {ConnectionId} joining scope {ScopeId}", connectionId, scopeId);

        // Add the current connection to the group identified by scopeId
        await Groups.AddToGroupAsync(connectionId, scopeId);

        _logger.LogInformation("Client {ConnectionId} joined scope {ScopeId}", connectionId, scopeId);
    }

    /// <summary>
    /// Removes the current connection from a SignalR group corresponding to the specified scope.
    /// </summary>
    /// <param name="scopeId">
    /// The scope identifier to leave. This corresponds to a channel name,
    /// conversation ID, or other scope identifier. Must not be null or empty.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="scopeId"/> is null or empty.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> This method removes the current client connection from a
    /// SignalR group identified by <paramref name="scopeId"/>. After leaving,
    /// the client will no longer receive messages broadcast to that scope.
    /// </para>
    /// <para>
    /// <b>Automatic Cleanup:</b> SignalR automatically removes connections from
    /// all groups when the client disconnects. This method is provided for
    /// explicit leave operations when a user wants to leave a specific scope
    /// without disconnecting entirely.
    /// </para>
    /// <para>
    /// <b>Idempotency:</b> Calling this method when the client is not in the
    /// group is safe. The operation will succeed with no effect.
    /// </para>
    /// <para>
    /// <b>Client Usage:</b>
    /// <code><![CDATA[
    /// await connection.invoke("LeaveScopeAsync", "general");
    /// // Client no longer receives messages from "general" scope
    /// ]]></code>
    /// </para>
    /// </remarks>
    public async Task LeaveScopeAsync(string scopeId)
    {
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            throw new ArgumentNullException(nameof(scopeId));
        }

        var connectionId = Context.ConnectionId;
        _logger.LogDebug("Client {ConnectionId} leaving scope {ScopeId}", connectionId, scopeId);

        // Remove the current connection from the group identified by scopeId
        await Groups.RemoveFromGroupAsync(connectionId, scopeId);

        _logger.LogInformation("Client {ConnectionId} left scope {ScopeId}", connectionId, scopeId);
    }

    /// <summary>
    /// Processes a chat message send request from the client.
    /// </summary>
    /// <param name="request">
    /// The chat send request containing the message details. Must not be null.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="request"/> is null.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> This method is the entry point for sending messages through
    /// the SignalR hub. It validates the request, processes it through the
    /// command handler, and handles the result.
    /// </para>
    /// <para>
    /// <b>Processing Flow:</b>
    /// <list type="number">
    /// <item>Validate the request is not null</item>
    /// <item>Extract sender ID from connection context (e.g., from auth claims)</item>
    /// <item>Create <see cref="SendChatMessageCommand"/> with sender ID and request</item>
    /// <item>Invoke <see cref="SendChatMessageCommandHandler"/> to process the command</item>
    /// <item>Handle success or failure result</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Sender ID Extraction:</b> In this implementation, the sender ID is
    /// extracted from the authenticated user's claims. For unauthenticated
    /// connections, a placeholder or anonymous identifier should be used.
    /// This example uses the connection ID as a fallback for demonstration purposes.
    /// </para>
    /// <para>
    /// <b>Error Handling:</b> The method catches exceptions and returns errors
    /// to the client via SignalR. In production, consider using typed SignalR
    /// responses or dedicated error callbacks.
    /// </para>
    /// <para>
    /// <b>Client Usage:</b>
    /// <code><![CDATA[
    /// await connection.invoke("SendAsync", {
    ///     scopeType: 0,
    ///     scopeId: "general",
    ///     text: "Hello, world!"
    /// });
    /// ]]></code>
    /// </para>
    /// </remarks>
    public async Task SendAsync(ChatSendRequestDto request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var connectionId = Context.ConnectionId;
        _logger.LogDebug(
            "Client {ConnectionId} sending message to scope {ScopeId}",
            connectionId,
            request.ScopeId);

        var senderId = Context.GetUserId();

        var command = new SendChatMessageCommand
        {
            SenderId = senderId,
            Request = request
        };

        try
        {
            // Process the command through the application layer
            var result = await _sendChatMessageCommandHandler.HandleAsync(
                command,
                Context.ConnectionAborted); // ConnectionAborted is never null in SignalR

            if (result.IsSuccess)
            {
                var enrichedEvent = result.Value!;
                var chatEvent = enrichedEvent.ChatEvent;
                _logger.LogInformation(
                    "Message {MessageId} sent successfully by {SenderId} to scope {ScopeId}",
                    chatEvent.MessageId,
                    senderId,
                    request.ScopeId);

                // Broadcast the message to all clients in the scope
                await Clients.Group(request.ScopeId).SendAsync(
                    "ReceiveMessage",
                    chatEvent);
            }
            else
            {
                var errorMessage = result.Error?.Message ?? "Unknown error";
                _logger.LogWarning(
                    "Message send failed for {SenderId}: {Error}",
                    senderId,
                    errorMessage);

                // Send error back to the client
                await Clients.Caller.SendAsync(
                    "ReceiveError",
                    errorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error processing message from {SenderId}",
                senderId);

            // Send error back to the client
            await Clients.Caller.SendAsync(
                "ReceiveError",
                "An unexpected error occurred. Please try again.");
        }
    }

    /// <summary>
    /// Called when a new connection is established to the hub.
    /// </summary>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> This method is overridden to register the user's presence
    /// when they connect. It tracks the connection in Redis, enabling:
    /// <list type="bullet">
    /// <item>Online/offline status across all pods</item>
    /// <item>Multi-connection tracking (same user on multiple devices)</item>
    /// <item>Automatic cleanup via TTL for abrupt disconnects</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Presence Registration:</b> The connection is registered with the user ID
    /// (extracted from claims or connection ID) and the unique connection ID.
    /// This allows tracking multiple connections per user.
    /// </para>
    /// <para>
    /// <b>Error Handling:</b> Presence registration failures are logged but do not
    /// prevent the connection from succeeding. This ensures the chat remains
    /// functional even if Redis is temporarily unavailable.
    /// </para>
    /// </remarks>
    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        var userId = Context.GetUserId();
        var user = Context.GetUserName();

        _logger.LogInformation(
            "Client connected. ConnectionId: {ConnectionId}, UserId: {UserId}, User: {User}",
            connectionId,
            userId,
            user);

        try
        {
            // Register the user's presence in Redis
            await _presenceService.SetOnlineAsync(userId, connectionId, Context.ConnectionAborted);

            _logger.LogDebug(
                "Presence registered for user {UserId} with connection {ConnectionId}",
                userId,
                connectionId);
        }
        catch (Exception ex)
        {
            // Log presence registration failure but don't fail the connection
            _logger.LogError(
                ex,
                "Failed to register presence for user {UserId} with connection {ConnectionId}",
                userId,
                connectionId);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a connection is disconnected from the hub.
    /// </summary>
    /// <param name="exception">
    /// The exception that occurred during disconnect, if any.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> This method is overridden to remove the user's presence
    /// when they disconnect. It updates Redis to track the disconnection.
    /// </para>
    /// <para>
    /// <b>Presence Cleanup:</b> The connection is removed from the user's presence
    /// set. If this was the last connection, the user is marked as offline.
    /// </para>
    /// <para>
    /// <b>Graceful vs Abrupt Disconnect:</b>
    /// <list type="bullet">
    /// <item><b>Graceful:</b> User explicitly disconnects (logout, close tab with notification)</item>
    /// <item><b>Abrupt:</b> Network failure, browser crash, tab close without notification</item>
    /// </list>
    /// For abrupt disconnects, the presence key will expire via TTL after 60 seconds.
    /// </para>
    /// <para>
    /// <b>Error Handling:</b> Presence cleanup failures are logged but do not
    /// affect the disconnect process. TTL will eventually clean up orphaned keys.
    /// </para>
    /// <para>
    /// <b>SignalR Group Cleanup:</b> SignalR automatically removes disconnected
    /// clients from all groups. No manual group cleanup is required.
    /// </para>
    /// </remarks>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        var userId = Context.GetUserId();
        var user = Context.GetUserName();

        if (exception is not null)
        {
            _logger.LogWarning(
                exception,
                "Client disconnected with error. ConnectionId: {ConnectionId}, UserId: {UserId}, User: {User}",
                connectionId,
                userId,
                user);
        }
        else
        {
            _logger.LogInformation(
                "Client disconnected. ConnectionId: {ConnectionId}, UserId: {UserId}, User: {User}",
                connectionId,
                userId,
                user);
        }

        try
        {
            // Remove the user's presence from Redis
            await _presenceService.SetOfflineAsync(userId, connectionId, Context.ConnectionAborted);

            _logger.LogDebug(
                "Presence removed for user {UserId} with connection {ConnectionId}",
                userId,
                connectionId);
        }
        catch (Exception ex)
        {
            // Log presence cleanup failure but don't fail the disconnect
            _logger.LogError(
                ex,
                "Failed to remove presence for user {UserId} with connection {ConnectionId}",
                userId,
                connectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
