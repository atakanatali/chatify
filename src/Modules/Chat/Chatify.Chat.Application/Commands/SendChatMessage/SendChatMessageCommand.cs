using Chatify.Chat.Application.Dtos;

namespace Chatify.Chat.Application.Commands.SendChatMessage;

/// <summary>
/// Represents a command to send a chat message within the Chatify system.
/// This command encapsulates all information needed to process a message
/// send operation, including sender identification and message content.
/// </summary>
/// <remarks>
/// <para>
/// <b>Command Pattern:</b> This class follows the Command pattern in Clean
/// Architecture, representing an intent to perform an action. Commands are
/// handled by dedicated handler classes that contain the business logic
/// for executing the command.
/// </para>
/// <para>
/// <b>Immutability:</b> As a record type with init-only properties,
/// SendChatMessageCommand is immutable after creation. This ensures the
/// command cannot be modified during processing, supporting predictable
/// behavior and easier testing.
/// </para>
/// <para>
/// <b>Validation:</b> The command handler validates all inputs against
/// domain policy before processing. Invalid commands will result in
/// failure results rather than exceptions.
/// </para>
/// <para>
/// <b>Separation of Concerns:</b> The sender ID is separate from the
/// request DTO because the sender is determined by authentication context
/// (e.g., JWT claims) at the API layer, while the request DTO contains
/// the user-provided message details.
/// </para>
/// </remarks>
public record SendChatMessageCommand
{
    /// <summary>
    /// Gets the unique identifier of the user sending this message.
    /// </summary>
    /// <value>
    /// A string uniquely identifying the sender within the Chatify system.
    /// This typically corresponds to a user ID from the identity provider.
    /// </value>
    /// <remarks>
    /// <para>
    /// The sender ID is extracted from authentication context (e.g., JWT claims,
    /// session data) by the API layer before creating the command. This ensures
    /// that users can only send messages as themselves, preventing spoofing.
    /// </para>
    /// <para>
    /// This value must pass validation by <see cref="Domain.ChatDomainPolicy.ValidateSenderId"/>
    /// before the message is processed. The validation enforces length constraints
    /// (1-256 characters) and ensures the value is not whitespace-only.
    /// </para>
    /// </remarks>
    public required string SenderId { get; init; }

    /// <summary>
    /// Gets the chat message send request containing message details.
    /// </summary>
    /// <value>
    /// A <see cref="ChatSendRequestDto"/> containing the message's scope type,
    /// scope identifier, and text content.
    /// </value>
    /// <remarks>
    /// <para>
    /// The request DTO encapsulates the user-provided message details:
    /// <list type="bullet">
    /// <item><see cref="ChatSendRequestDto.ScopeType"/> - Channel or DirectMessage</item>
    /// <item><see cref="ChatSendRequestDto.ScopeId"/> - Target conversation identifier</item>
    /// <item><see cref="ChatSendRequestDto.Text"/> - Message content</item>
    /// </list>
    /// </para>
    /// <para>
    /// All properties within the request DTO are validated against domain
    /// policy by the command handler before the message is produced.
    /// </para>
    /// </remarks>
    public required ChatSendRequestDto Request { get; init; }
}
