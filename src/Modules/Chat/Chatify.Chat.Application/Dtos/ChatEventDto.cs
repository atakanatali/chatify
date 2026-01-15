using Chatify.Chat.Domain;

namespace Chatify.Chat.Application.Dtos;

/// <summary>
/// Data transfer object representing a chat event that occurs within the Chatify system.
/// Chat events represent the core unit of communication and are produced, transmitted,
/// and consumed throughout the system to enable real-time messaging.
/// </summary>
/// <remarks>
/// <para>
/// <b>Event Lifecycle:</b> A ChatEventDto is created when a message is sent,
/// transmitted through the messaging infrastructure (e.g., message brokers like Kafka
/// or Redpanda), and ultimately consumed by clients listening for chat events.
/// Each event represents an immutable fact that a message was sent at a specific time.
/// </para>
/// <para>
/// <b>Scope-Based Ordering:</b> Events with the same <see cref="ChatEventDto.ScopeType"/>
/// and <see cref="ChatEventDto.ScopeId"/> are ordered together by <see cref="ChatEventDto.CreatedAtUtc"/>
/// to ensure all participants see messages in the same chronological sequence.
/// This is critical for maintaining conversation coherence.
/// </para>
/// <para>
/// <b>Origin Tracking:</b> The <see cref="OriginPodId"/> property identifies
/// which pod originally handled the message creation request, supporting
/// debugging, audit trails, and distributed tracing in Kubernetes deployments.
/// </para>
/// <para>
/// <b>Immutability:</b> As a record type, ChatEventDto is inherently immutable.
/// Once created, an event should never be modified. If message editing is
/// supported, it should be represented as a new event with a reference to
/// the original message ID.
/// </para>
/// </remarks>
public record ChatEventDto
{
    /// <summary>
    /// Gets the unique identifier for this chat event.
    /// </summary>
    /// <value>
    /// A GUID that uniquely identifies this event across the entire Chatify system.
    /// This identifier is generated at event creation time and never reused.
    /// </value>
    /// <remarks>
    /// <para>
    /// The MessageId serves as the primary key for this event in persistence
    /// layers and is used for message deduplication and idempotent operations.
    /// </para>
    /// <para>
    /// When supporting message edits or deletions, this ID is used to reference
    /// the original message being modified.
    /// </para>
    /// </remarks>
    public required Guid MessageId { get; init; }

    /// <summary>
    /// Gets the type of chat scope this event belongs to.
    /// </summary>
    /// <value>
    /// A <see cref="ChatScopeTypeEnum"/> value indicating whether this event
    /// belongs to a Channel or DirectMessage scope. This determines event
    /// routing, which participants can access it, and how ordering is applied.
    /// </value>
    /// <remarks>
    /// The scope type is critical for determining how the event is distributed
    /// to listeners. Channel events are broadcast to all channel members,
    /// while DirectMessage events are sent only to the conversation participants.
    /// </remarks>
    public required ChatScopeTypeEnum ScopeType { get; init; }

    /// <summary>
    /// Gets the scope identifier that groups this event with related events.
    /// </summary>
    /// <value>
    /// A string identifying the specific scope within the scope type.
    /// For channels, this may be a channel name or UUID.
    /// For direct messages, this may be a composite key derived from participant IDs.
    /// </value>
    /// <remarks>
    /// <para>
    /// The ScopeId, combined with ScopeType, defines the complete ordering context
    /// for an event. All events sharing the same ScopeType and ScopeId are
    /// processed in strict chronological order to maintain conversation integrity.
    /// </para>
    /// <para>
    /// When consuming events from a message broker (e.g., Kafka, Redpanda), events
    /// with the same ScopeId should be processed sequentially to preserve ordering,
    /// while events with different ScopeIds can be processed in parallel.
    /// </para>
    /// </remarks>
    public required string ScopeId { get; init; }

    /// <summary>
    /// Gets the identifier of the user who sent this message.
    /// </summary>
    /// <value>
    /// A string uniquely identifying the sender within the Chatify system.
    /// This typically corresponds to a user ID from the identity provider.
    /// </value>
    /// <remarks>
    /// <para>
    /// The SenderId is used for authorization (ensuring only the sender can
    /// delete their messages), display purposes (showing who sent each message),
    /// and access control (determining which scopes a user can participate in).
    /// </para>
    /// <para>
    /// When displaying events to users, this ID should be resolved to a display
    /// name or profile information through a separate user service lookup.
    /// </para>
    /// </remarks>
    public required string SenderId { get; init; }

    /// <summary>
    /// Gets the actual text content of the message.
    /// </summary>
    /// <value>
    /// The message text as provided by the sender. May include Unicode characters,
    /// emoji, and other text content. Empty strings are permitted for messages
    /// with only attachments.
    /// </value>
    /// <remarks>
    /// <para>
    /// This text has already been validated against domain policy constraints
    /// (maximum length defined by <see cref="Domain.ChatDomainPolicy.MaxTextLength"/>)
    /// before being stored in this event.
    /// </para>
    /// <para>
    /// When displaying this text to users, appropriate sanitization should be
    /// applied based on the rendering context (HTML, Markdown, plain text)
    /// to prevent XSS attacks and ensure proper formatting.
    /// </para>
    /// </remarks>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when this message was created.
    /// </summary>
    /// <value>
    /// A <see cref="DateTime"/> value in UTC representing when the message
    /// was originally created. This value is set at message creation time
    /// and never modified.
    /// </value>
    /// <remarks>
    /// <para>
    /// The CreatedAtUtc timestamp is the primary ordering key for events
    /// within a scope. Events are processed in strictly increasing order
    /// of CreatedAtUtc values to ensure all participants see the same sequence.
    /// </para>
    /// <para>
    /// When displaying events, this timestamp should be formatted relative
    /// to the user's local timezone for better readability.
    /// </para>
    /// <para>
    /// This timestamp uses high-precision time (including ticks) to handle
    /// cases where multiple messages are created in rapid succession.
    /// In cases of identical timestamps, MessageId serves as a tiebreaker.
    /// </para>
    /// </remarks>
    public required DateTime CreatedAtUtc { get; init; }

    /// <summary>
    /// Gets the identifier of the pod that originally created this message.
    /// </summary>
    /// <value>
    /// A string identifying the Kubernetes pod (or equivalent deployment unit)
    /// that handled the initial message creation request.
    /// </value>
    /// <remarks>
    /// <para>
    /// The OriginPodId supports operational concerns including debugging,
    /// distributed tracing, and audit logging. When investigating issues,
    /// knowing which pod handled a request helps correlate event flow
    /// with logs and metrics from specific instances.
    /// </para>
    /// <para>
    /// In Kubernetes deployments, this typically contains the pod name
    /// (e.g., "chat-api-7d9f4c5b6d-abc12"). In development environments,
    /// it may contain a machine name or other identifier.
    /// </para>
    /// <para>
    /// This field is populated from infrastructure context at event creation
    /// time and should not be provided by end users. It is included in the
    /// event for observability and debugging purposes.
    /// </para>
    /// </remarks>
    public required string OriginPodId { get; init; }
}
