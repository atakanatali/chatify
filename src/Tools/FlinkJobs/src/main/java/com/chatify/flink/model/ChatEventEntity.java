package com.chatify.flink.model;

import com.google.gson.Gson;
import com.google.gson.annotations.SerializedName;

import java.time.Instant;
import java.time.LocalDateTime;
import java.time.ZoneOffset;
import java.util.Objects;
import java.util.UUID;

/**
 * Entity representing a chat event that flows through the Chatify system.
 * <p>
 * This class mirrors the {@code ChatEventDto} from the Chatify.Chat.Application layer
 * and represents the core unit of communication consumed from Kafka for stream processing.
 * </p>
 * <p>
 * <b>Event Lifecycle:</b> ChatEventEntity instances are created when deserializing
 * messages from the {@code chat-events} Kafka topic. Each event represents an immutable
 * fact that a message was sent at a specific time.
 * </p>
 * <p>
 * <b>Scope-Based Ordering:</b> Events with the same {@code scopeType} and {@code scopeId}
 * are ordered together by {@code createdAtUtc} to ensure all participants see messages
 * in the same chronological sequence. This is critical for maintaining conversation coherence.
 * </p>
 * <p>
 * <b>Immutability:</b> This class is designed to be immutable. Once created, an event
 * should never be modified. This ensures thread safety in the Flink streaming environment.
 * </p>
 * <p>
 * <b>Serialization:</b> Events are serialized to JSON in Kafka using the format defined
 * by the C# ChatEventDto. Field names use snake_case to match the C# JSON serialization.
 * </p>
 *
 * @see ChatScopeTypeEnum
 * @see AnalyticsEventEntity
 * @see RateLimitEventEntity
 * @since 1.0.0
 */
public class ChatEventEntity {

    /**
     * Unique identifier for this chat event.
     * <p>
     * This GUID uniquely identifies this event across the entire Chatify system.
     * It is generated at event creation time and never reused.
     * </p>
     */
    @SerializedName("messageId")
    private final UUID messageId;

    /**
     * The type of chat scope this event belongs to.
     * <p>
     * Determines event routing, which participants can access it, and how ordering is applied.
     * </p>
     */
    @SerializedName("scopeType")
    private final ChatScopeTypeEnum scopeType;

    /**
     * The scope identifier that groups this event with related events.
     * <p>
     * For channels, this may be a channel name or UUID (e.g., "general").
     * For direct messages, this may be a composite key derived from participant IDs
     * (e.g., "user1-user2").
     * </p>
     */
    @SerializedName("scopeId")
    private final String scopeId;

    /**
     * The identifier of the user who sent this message.
     * <p>
     * This typically corresponds to a user ID from the identity provider.
     * </p>
     */
    @SerializedName("senderId")
    private final String senderId;

    /**
     * The actual text content of the message.
     * <p>
     * May include Unicode characters, emoji, and other text content.
     * Empty strings are permitted for messages with only attachments.
     * </p>
     */
    @SerializedName("text")
    private final String text;

    /**
     * The UTC timestamp when this message was created.
     * <p>
     * Represented as an ISO-8601 string in JSON (e.g., "2026-01-15T10:30:00Z").
     * This timestamp is the primary ordering key for events within a scope.
     * </p>
     */
    @SerializedName("createdAtUtc")
    private final String createdAtUtc;

    /**
     * The identifier of the pod that originally created this message.
     * <p>
     * In Kubernetes deployments, this typically contains the pod name
     * (e.g., "chat-api-7d9f4c5b6d-abc12").
     * </p>
     */
    @SerializedName("originPodId")
    private final String originPodId;

    /**
     * GSON instance for JSON serialization/deserialization.
     */
    private static final Gson GSON = new Gson();

    /**
     * Constructs a new ChatEventEntity with all required fields.
     *
     * @param messageId    The unique identifier for this event.
     * @param scopeType    The type of chat scope (Channel or DirectMessage).
     * @param scopeId      The scope identifier for grouping related events.
     * @param senderId     The user ID who sent the message.
     * @param text         The message content.
     * @param createdAtUtc The ISO-8601 UTC timestamp when the message was created.
     * @param originPodId  The pod identifier that created this message.
     */
    public ChatEventEntity(
            UUID messageId,
            ChatScopeTypeEnum scopeType,
            String scopeId,
            String senderId,
            String text,
            String createdAtUtc,
            String originPodId) {
        this.messageId = Objects.requireNonNull(messageId, "messageId must not be null");
        this.scopeType = Objects.requireNonNull(scopeType, "scopeType must not be null");
        this.scopeId = Objects.requireNonNull(scopeId, "scopeId must not be null");
        this.senderId = Objects.requireNonNull(senderId, "senderId must not be null");
        this.text = Objects.requireNonNull(text, "text must not be null");
        this.createdAtUtc = Objects.requireNonNull(createdAtUtc, "createdAtUtc must not be null");
        this.originPodId = Objects.requireNonNull(originPodId, "originPodId must not be null");
    }

    /**
     * Deserializes a JSON string to a ChatEventEntity.
     * <p>
     * This method is used by the Flink Kafka deserializer to convert
     * Kafka message values to ChatEventEntity instances.
     * </p>
     *
     * @param json The JSON string to deserialize.
     * @return The deserialized ChatEventEntity.
     * @throws com.google.gson.JsonSyntaxException if the JSON is malformed.
     */
    public static ChatEventEntity fromJson(String json) {
        return GSON.fromJson(json, ChatEventEntity.class);
    }

    /**
     * Serializes this ChatEventEntity to a JSON string.
     *
     * @return The JSON representation of this event.
     */
    public String toJson() {
        return GSON.toJson(this);
    }

    /**
     * Gets the unique identifier for this chat event.
     *
     * @return The message ID as a UUID.
     */
    public UUID getMessageId() {
        return messageId;
    }

    /**
     * Gets the type of chat scope this event belongs to.
     *
     * @return The scope type (Channel or DirectMessage).
     */
    public ChatScopeTypeEnum getScopeType() {
        return scopeType;
    }

    /**
     * Gets the scope identifier that groups this event with related events.
     *
     * @return The scope ID string.
     */
    public String getScopeId() {
        return scopeId;
    }

    /**
     * Gets the identifier of the user who sent this message.
     *
     * @return The sender ID string.
     */
    public String getSenderId() {
        return senderId;
    }

    /**
     * Gets the actual text content of the message.
     *
     * @return The message text.
     */
    public String getText() {
        return text;
    }

    /**
     * Gets the UTC timestamp when this message was created.
     *
     * @return The ISO-8601 timestamp string.
     */
    public String getCreatedAtUtc() {
        return createdAtUtc;
    }

    /**
     * Gets the creation timestamp as an Instant for time-based operations.
     * <p>
     * This is useful for windowing operations in Flink.
     * </p>
     *
     * @return The creation timestamp as an Instant.
     */
    public Instant getCreatedAtAsInstant() {
        return Instant.parse(createdAtUtc);
    }

    /**
     * Gets the identifier of the pod that originally created this message.
     *
     * @return The origin pod ID string.
     */
    public String getOriginPodId() {
        return originPodId;
    }

    /**
     * Computes the partition key for Kafka partitioning.
     * <p>
     * The scopeId is used as the partition key to ensure all messages
     * for the same scope are routed to the same partition, maintaining
     * strict ordering within scopes.
     * </p>
     *
     * @return The partition key (scopeId).
     */
    public String getPartitionKey() {
        return scopeId;
    }

    /**
     * Computes a composite scope identifier for analytics aggregation.
     * <p>
     * This combines scopeType and scopeId in the format "type:id"
     * for use in aggregations that need to distinguish between
     * channel and direct message scopes with the same ID.
     * </p>
     *
     * @return The composite scope identifier.
     */
    public String getCompositeScopeId() {
        return scopeType.getValue() + ":" + scopeId;
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (o == null || getClass() != o.getClass()) return false;
        ChatEventEntity that = (ChatEventEntity) o;
        return Objects.equals(messageId, that.messageId);
    }

    @Override
    public int hashCode() {
        return Objects.hash(messageId);
    }

    @Override
    public String toString() {
        return "ChatEventEntity{" +
                "messageId=" + messageId +
                ", scopeType=" + scopeType +
                ", scopeId='" + scopeId + '\'' +
                ", senderId='" + senderId + '\'' +
                ", text='" + text + '\'' +
                ", createdAtUtc='" + createdAtUtc + '\'' +
                ", originPodId='" + originPodId + '\'' +
                '}';
    }
}
