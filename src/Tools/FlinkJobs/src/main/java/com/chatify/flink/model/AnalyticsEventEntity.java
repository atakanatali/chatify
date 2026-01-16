package com.chatify.flink.model;

import com.google.gson.Gson;
import com.google.gson.annotations.SerializedName;

import java.time.Instant;
import java.util.Objects;
import java.util.UUID;

/**
 * Entity representing an analytics event derived from chat message processing.
 * <p>
 * This class represents aggregated analytics data computed from chat events,
 * designed to be written to the {@code analytics-events} Kafka topic for
 * consumption by monitoring dashboards, alerting systems, and business intelligence tools.
 * </p>
 * <p>
 * <b>Derivation:</b> AnalyticsEventEntity instances are created by the Flink job
 * by aggregating ChatEventEntity instances over time windows. They contain
 * computed metrics rather than raw message data.
 * </p>
 * <p>
 * <b>Time Windows:</b> Analytics events are produced for tumbling or sliding
 * time windows (e.g., 1 minute, 5 minutes, 1 hour) to provide insights into
 * chat system activity patterns.
 * </p>
 * <p>
 * <b>Use Cases:</b>
 * <ul>
 *   <li>Real-time dashboards showing message volume by scope</li>
 *   <li>Alerting on unusual activity spikes</li>
 *   <li>Capacity planning based on usage patterns</li>
 *   <li>User engagement analytics</li>
 * </ul>
 * </p>
 *
 * @see ChatEventEntity
 * @see RateLimitEventEntity
 * @since 1.0.0
 */
public class AnalyticsEventEntity {

    /**
     * Unique identifier for this analytics event.
     * <p>
     * Generated when the analytics event is created to ensure
     * each aggregated window has a unique identifier.
     * </p>
     */
    @SerializedName("analyticsId")
    private final UUID analyticsId;

    /**
     * The type of chat scope this analytics applies to.
     */
    @SerializedName("scopeType")
    private final ChatScopeTypeEnum scopeType;

    /**
     * The scope identifier for this analytics data.
     */
    @SerializedName("scopeId")
    private final String scopeId;

    /**
     * The start timestamp of the aggregation window (ISO-8601 UTC).
     */
    @SerializedName("windowStartUtc")
    private final String windowStartUtc;

    /**
     * The end timestamp of the aggregation window (ISO-8601 UTC).
     */
    @SerializedName("windowEndUtc")
    private final String windowEndUtc;

    /**
     * The duration of the aggregation window in seconds.
     */
    @SerializedName("windowDurationSeconds")
    private final long windowDurationSeconds;

    /**
     * The total number of messages in this window.
     */
    @SerializedName("messageCount")
    private final long messageCount;

    /**
     * The number of unique users who sent messages in this window.
     */
    @SerializedName("activeUserCount")
    private final long activeUserCount;

    /**
     * The number of unique senders in this window.
     * <p>
     * In most cases, this equals activeUserCount, but may differ
     * if bot accounts or system users are tracked separately.
     * </p>
     */
    @SerializedName("uniqueSenderCount")
    private final long uniqueSenderCount;

    /**
     * The total number of characters across all messages in this window.
     */
    @SerializedName("totalCharacterCount")
    private final long totalCharacterCount;

    /**
     * The average message length in characters for this window.
     */
    @SerializedName("averageMessageLength")
    private final double averageMessageLength;

    /**
     * The timestamp when this analytics event was created (ISO-8601 UTC).
     */
    @SerializedName("computedAtUtc")
    private final String computedAtUtc;

    /**
     * The identifier of the Flink task that computed this analytics event.
     */
    @SerializedName("computeTaskId")
    private final String computeTaskId;

    /**
     * GSON instance for JSON serialization.
     */
    private static final Gson GSON = new Gson();

    /**
     * Constructs a new AnalyticsEventEntity with all computed fields.
     *
     * @param analyticsId           The unique identifier for this analytics event.
     * @param scopeType              The scope type (Channel or DirectMessage).
     * @param scopeId                The scope identifier.
     * @param windowStartUtc         The window start timestamp as ISO-8601 string.
     * @param windowEndUtc           The window end timestamp as ISO-8601 string.
     * @param windowDurationSeconds  The window duration in seconds.
     * @param messageCount           The total message count in the window.
     * @param activeUserCount        The number of active users in the window.
     * @param uniqueSenderCount      The number of unique senders in the window.
     * @param totalCharacterCount    The total character count across all messages.
     * @param averageMessageLength   The average message length in characters.
     * @param computedAtUtc          The timestamp when this analytics was computed.
     * @param computeTaskId          The Flink task identifier that computed this.
     */
    public AnalyticsEventEntity(
            UUID analyticsId,
            ChatScopeTypeEnum scopeType,
            String scopeId,
            String windowStartUtc,
            String windowEndUtc,
            long windowDurationSeconds,
            long messageCount,
            long activeUserCount,
            long uniqueSenderCount,
            long totalCharacterCount,
            double averageMessageLength,
            String computedAtUtc,
            String computeTaskId) {
        this.analyticsId = Objects.requireNonNull(analyticsId, "analyticsId must not be null");
        this.scopeType = Objects.requireNonNull(scopeType, "scopeType must not be null");
        this.scopeId = Objects.requireNonNull(scopeId, "scopeId must not be null");
        this.windowStartUtc = Objects.requireNonNull(windowStartUtc, "windowStartUtc must not be null");
        this.windowEndUtc = Objects.requireNonNull(windowEndUtc, "windowEndUtc must not be null");
        this.windowDurationSeconds = windowDurationSeconds;
        this.messageCount = messageCount;
        this.activeUserCount = activeUserCount;
        this.uniqueSenderCount = uniqueSenderCount;
        this.totalCharacterCount = totalCharacterCount;
        this.averageMessageLength = averageMessageLength;
        this.computedAtUtc = Objects.requireNonNull(computedAtUtc, "computedAtUtc must not be null");
        this.computeTaskId = Objects.requireNonNull(computeTaskId, "computeTaskId must not be null");
    }

    /**
     * Creates a builder for constructing AnalyticsEventEntity instances.
     *
     * @return A new AnalyticsEventEntityBuilder.
     */
    public static AnalyticsEventEntityBuilder builder() {
        return new AnalyticsEventEntityBuilder();
    }

    /**
     * Serializes this AnalyticsEventEntity to a JSON string for Kafka production.
     *
     * @return The JSON representation of this analytics event.
     */
    public String toJson() {
        return GSON.toJson(this);
    }

    // Getters

    public UUID getAnalyticsId() {
        return analyticsId;
    }

    public ChatScopeTypeEnum getScopeType() {
        return scopeType;
    }

    public String getScopeId() {
        return scopeId;
    }

    public String getWindowStartUtc() {
        return windowStartUtc;
    }

    public String getWindowEndUtc() {
        return windowEndUtc;
    }

    public long getWindowDurationSeconds() {
        return windowDurationSeconds;
    }

    public long getMessageCount() {
        return messageCount;
    }

    public long getActiveUserCount() {
        return activeUserCount;
    }

    public long getUniqueSenderCount() {
        return uniqueSenderCount;
    }

    public long getTotalCharacterCount() {
        return totalCharacterCount;
    }

    public double getAverageMessageLength() {
        return averageMessageLength;
    }

    public String getComputedAtUtc() {
        return computedAtUtc;
    }

    public String getComputeTaskId() {
        return computeTaskId;
    }

    /**
     * Gets the partition key for Kafka production.
     * <p>
     * Uses the scopeId as the partition key to ensure all analytics
     * for the same scope are written to the same partition.
     * </p>
     *
     * @return The partition key (scopeId).
     */
    public String getPartitionKey() {
        return scopeId;
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (o == null || getClass() != o.getClass()) return false;
        AnalyticsEventEntity that = (AnalyticsEventEntity) o;
        return Objects.equals(analyticsId, that.analyticsId);
    }

    @Override
    public int hashCode() {
        return Objects.hash(analyticsId);
    }

    @Override
    public String toString() {
        return "AnalyticsEventEntity{" +
                "analyticsId=" + analyticsId +
                ", scopeType=" + scopeType +
                ", scopeId='" + scopeId + '\'' +
                ", windowStartUtc='" + windowStartUtc + '\'' +
                ", windowEndUtc='" + windowEndUtc + '\'' +
                ", windowDurationSeconds=" + windowDurationSeconds +
                ", messageCount=" + messageCount +
                ", activeUserCount=" + activeUserCount +
                ", uniqueSenderCount=" + uniqueSenderCount +
                ", totalCharacterCount=" + totalCharacterCount +
                ", averageMessageLength=" + averageMessageLength +
                ", computedAtUtc='" + computedAtUtc + '\'' +
                ", computeTaskId='" + computeTaskId + '\'' +
                '}';
    }

    /**
     * Builder class for constructing AnalyticsEventEntity instances.
     */
    public static class AnalyticsEventEntityBuilder {
        private UUID analyticsId;
        private ChatScopeTypeEnum scopeType;
        private String scopeId;
        private String windowStartUtc;
        private String windowEndUtc;
        private long windowDurationSeconds;
        private long messageCount;
        private long activeUserCount;
        private long uniqueSenderCount;
        private long totalCharacterCount;
        private double averageMessageLength;
        private String computedAtUtc;
        private String computeTaskId;

        public AnalyticsEventEntityBuilder analyticsId(UUID analyticsId) {
            this.analyticsId = analyticsId;
            return this;
        }

        public AnalyticsEventEntityBuilder scopeType(ChatScopeTypeEnum scopeType) {
            this.scopeType = scopeType;
            return this;
        }

        public AnalyticsEventEntityBuilder scopeId(String scopeId) {
            this.scopeId = scopeId;
            return this;
        }

        public AnalyticsEventEntityBuilder windowStartUtc(String windowStartUtc) {
            this.windowStartUtc = windowStartUtc;
            return this;
        }

        public AnalyticsEventEntityBuilder windowEndUtc(String windowEndUtc) {
            this.windowEndUtc = windowEndUtc;
            return this;
        }

        public AnalyticsEventEntityBuilder windowDurationSeconds(long windowDurationSeconds) {
            this.windowDurationSeconds = windowDurationSeconds;
            return this;
        }

        public AnalyticsEventEntityBuilder messageCount(long messageCount) {
            this.messageCount = messageCount;
            return this;
        }

        public AnalyticsEventEntityBuilder activeUserCount(long activeUserCount) {
            this.activeUserCount = activeUserCount;
            return this;
        }

        public AnalyticsEventEntityBuilder uniqueSenderCount(long uniqueSenderCount) {
            this.uniqueSenderCount = uniqueSenderCount;
            return this;
        }

        public AnalyticsEventEntityBuilder totalCharacterCount(long totalCharacterCount) {
            this.totalCharacterCount = totalCharacterCount;
            return this;
        }

        public AnalyticsEventEntityBuilder averageMessageLength(double averageMessageLength) {
            this.averageMessageLength = averageMessageLength;
            return this;
        }

        public AnalyticsEventEntityBuilder computedAtUtc(String computedAtUtc) {
            this.computedAtUtc = computedAtUtc;
            return this;
        }

        public AnalyticsEventEntityBuilder computeTaskId(String computeTaskId) {
            this.computeTaskId = computeTaskId;
            return this;
        }

        /**
         * Builds the AnalyticsEventEntity with the configured values.
         *
         * @return A new AnalyticsEventEntity instance.
         */
        public AnalyticsEventEntity build() {
            return new AnalyticsEventEntity(
                    analyticsId,
                    scopeType,
                    scopeId,
                    windowStartUtc,
                    windowEndUtc,
                    windowDurationSeconds,
                    messageCount,
                    activeUserCount,
                    uniqueSenderCount,
                    totalCharacterCount,
                    averageMessageLength,
                    computedAtUtc,
                    computeTaskId
            );
        }
    }
}
