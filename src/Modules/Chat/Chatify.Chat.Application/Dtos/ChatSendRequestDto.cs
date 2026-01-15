using Chatify.Chat.Domain;

namespace Chatify.Chat.Application.Dtos;

/// <summary>
/// Data transfer object representing a request to send a chat message.
/// This DTO encapsulates the minimum required information for creating
/// and transmitting a chat message within the Chatify system.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope Context:</b> The combination of <see cref="ChatSendRequestDto.ScopeType"/> and
/// <see cref="ChatSendRequestDto.ScopeId"/> defines the conversation context where the message
/// will be sent. All messages sharing the same scope are ordered together
/// to maintain conversation integrity.
/// </para>
/// <para>
/// <b>Validation:</b> The <see cref="ChatSendRequestDto.Text"/> property is subject to domain
/// policy validation as defined by <see cref="ChatDomainPolicy"/>.
/// Messages exceeding the maximum length will be rejected before processing.
/// </para>
/// <para>
/// <b>Sender Context:</b> The sender identifier is not included in this DTO
/// as it is expected to be extracted from authentication context (e.g., JWT claims)
/// at the API layer and passed separately to the command handler.
/// </para>
/// </remarks>
public record ChatSendRequestDto
{
    /// <summary>
    /// Gets the type of chat scope this message belongs to.
    /// </summary>
    /// <value>
    /// A <see cref="ChatScopeTypeEnum"/> value indicating whether this message
    /// is destined for a Channel or DirectMessage scope. This determines
    /// message routing, participant access, and ordering behavior.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>Channel:</b> Messages are broadcast to all channel members.
    /// The ScopeId typically represents a channel name or UUID.
    /// </para>
    /// <para>
    /// <b>DirectMessage:</b> Messages are sent to specific participants.
    /// The ScopeId typically represents a conversation identifier.
    /// </para>
    /// </remarks>
    public required ChatScopeTypeEnum ScopeType { get; init; }

    /// <summary>
    /// Gets the scope identifier that targets this message to a specific conversation.
    /// </summary>
    /// <value>
    /// A string identifying the specific scope within the scope type.
    /// For channels, this may be a channel name (e.g., "general", "random")
    /// or UUID. For direct messages, this may be a conversation UUID or
    /// composite key derived from participant IDs.
    /// </value>
    /// <remarks>
    /// <para>
    /// The ScopeId, combined with ScopeType, uniquely identifies the target
    /// conversation for this message. All messages with the same ScopeType
    /// and ScopeId are ordered together to maintain conversation integrity.
    /// </para>
    /// <para>
    /// This value must pass validation by <see cref="ChatDomainPolicy.ValidateScopeId"/>
    /// before the message is processed. The validation enforces length constraints
    /// (1-256 characters) and ensures the value is not whitespace-only.
    /// </para>
    /// </remarks>
    public required string ScopeId { get; init; }

    /// <summary>
    /// Gets the text content of the message to be sent.
    /// </summary>
    /// <value>
    /// The message text as provided by the sender. May include Unicode characters,
    /// emoji, and other text content. Empty strings are permitted for messages
    /// that may contain only attachments or other non-text content.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>Length Constraints:</b> Message text is subject to the maximum length
    /// defined by <see cref="ChatDomainPolicy.MaxTextLength"/> (4096 characters).
    /// Messages exceeding this limit will be rejected during validation.
    /// </para>
    /// <para>
    /// <b>Content Storage:</b> The text is stored as-is without sanitization.
    /// Any HTML/Markdown rendering or XSS protection should be applied at
    /// the presentation layer when displaying messages to users.
    /// </para>
    /// <para>
    /// <b>Content Moderation:</b> This property only stores the text content.
    /// Content moderation, spam detection, and filtering should be handled
    /// by separate services in the processing pipeline.
    /// </para>
    /// </remarks>
    public required string Text { get; init; }
}
