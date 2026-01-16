using Chatify.Chat.Application.Commands.SendChatMessage;
using Chatify.Chat.Application.Dtos;
using Chatify.Chat.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Chatify.Api.Controllers;

/// <summary>
/// API controller for managing chat messages in the Chatify system.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This controller exposes HTTP endpoints for sending chat messages,
/// providing the primary interface for clients to interact with the chat functionality.
/// </para>
/// <para>
/// <b>Authentication:</b> In production, all endpoints should be protected with
/// authentication. The sender ID is extracted from the authentication context
/// (e.g., JWT claims) rather than provided by the client.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    /// <summary>
    /// Gets the command handler for sending chat messages.
    /// </summary>
    private readonly SendChatMessageCommandHandler _commandHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatController"/> class.
    /// </summary>
    /// <param name="commandHandler">
    /// The command handler for sending chat messages. Must not be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="commandHandler"/> is null.
    /// </exception>
    public ChatController(SendChatMessageCommandHandler commandHandler)
    {
        _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
    }

    /// <summary>
    /// Sends a chat message to the specified scope.
    /// </summary>
    /// <param name="senderId">
    /// The ID of the user sending the message. In production, this should be
    /// extracted from authentication context (e.g., JWT claims).
    /// </param>
    /// <param name="request">
    /// The chat message send request containing scope type, scope ID, and message text.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the request.
    /// </param>
    /// <returns>
    /// An <see cref="IActionResult"/> containing:
    /// <list type="bullet">
    /// <item>200 OK with the enriched chat event on success</item>
    /// <item>400 Bad Request for validation errors</item>
    /// <item>429 Too Many Requests when rate limit is exceeded</item>
    /// <item>503 Service Unavailable for event production failures</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Request Flow:</b>
    /// <list type="number">
    /// <item>Validate request parameters against domain policy</item>
    /// <item>Check rate limits for the sender</item>
    /// <item>Create chat event with origin pod tracking</item>
    /// <item>Produce event to message broker (Kafka or in-memory)</item>
    /// <item>Return enriched event with delivery metadata</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Example Request:</b>
    /// <code><![CDATA[
    /// POST /api/chat/send/user-123
    /// Content-Type: application/json
    ///
    /// {
    ///   "scopeType": 0,
    ///   "scopeId": "general",
    ///   "text": "Hello, world!"
    /// }
    /// ]]></code>
    /// </para>
    /// <para>
    /// <b>Example Response (Success):</b>
    /// <code><![CDATA[
    /// {
    ///   "chatEvent": {
    ///     "messageId": "550e8400-e29b-41d4-a716-446655440000",
    ///     "scopeType": 0,
    ///     "scopeId": "general",
    ///     "senderId": "user-123",
    ///     "text": "Hello, world!",
    ///     "createdAtUtc": "2025-01-15T10:30:00Z",
    ///     "originPodId": "chat-api-pod-123"
    ///   },
    ///   "partition": 0,
    ///   "offset": 123
    /// }
    /// ]]></code>
    /// </para>
    /// </remarks>
    [HttpPost("send/{senderId}")]
    public async Task<IActionResult> SendMessageAsync(
        [FromRoute] string senderId,
        [FromBody] ChatSendRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new SendChatMessageCommand
        {
            SenderId = senderId,
            Request = request
        };

        var result = await _commandHandler.HandleAsync(command, cancellationToken);

        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error?.Message, code = result.Error?.Code });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Health check endpoint for the Chat API.
    /// </summary>
    /// <returns>
    /// A 200 OK response indicating the Chat API is operational.
    /// </returns>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
