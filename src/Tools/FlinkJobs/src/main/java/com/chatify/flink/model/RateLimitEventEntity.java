package com.chatify.flink.model;

import com.google.gson.Gson;
import com.google.gson.annotations.SerializedName;

import java.time.Instant;
import java.util.Objects;
import java.util.UUID;

/**
 * Entity representing a rate limit event derived from high-frequency user activity.
 * <p>
 * This class represents rate limit violations or warnings computed from chat events,
 * designed to be written to the {@code rate-limit-events} Kafka topic for consumption
 * by monitoring systems, alerting tools, and enforcement mechanisms.
 * </p>
 * <p>
 * <b>Derivation:</b> RateLimitEventEntity instances are created by the Flink job
 * by monitoring message frequency per user over sliding time windows. When a user
 * exceeds configured thresholds, a rate limit event is generated.
 * </p>
 * <p>
 * <b>Enforcement:</b> These events can be consumed by enforcement services to
 * temporarily restrict user activity, send warnings, or trigger administrative review.
 * </p>
 * <p>
 * <b>Use Cases:</b>
 * <ul>
 *   <li>Detecting spam or bot activity</li>
 *   <li>Enforcing per-user rate limits</li>
 *   <li>Generating alerts for suspicious activity</li>
 *   <li>Feeding machine learning models for abuse detection</li>
 * </ul>
 * </p>
 *
 * @see ChatEventEntity
 * @see AnalyticsEventEntity
 * @since 1.0.0
 */
public class RateLimitEventEntity {

    /**
     * The type of rate limit event.
     */
    public enum RateLimitEventType {
        /**
         * The user has exceeded a warning threshold but is not yet blocked.
         */
        WARNING("Warning"),

        /**
         * The user has exceeded a hard limit and should be throttled.
         */
        THROTTLE("Throttle"),

        /**
         * The user has been flagged for potential abuse requiring admin review.
         */
        FLAG("Flag");

        private final String value;

        RateLimitEventType(String value) {
            this.value = value;
        }

        public String getValue() {
            return value;
        }

        public static RateLimitEventType fromValue(String value) {
            for (RateLimitEventType type : RateLimitEventType.values()) {
                if (type.value.equals(value)) {
                    return type;
                }
            }
            throw new IllegalArgumentException("Unknown RateLimitEventType value: " + value);
        }

        @Override
        public String toString() {
            return value;
        }
    }

    /**
     * Unique identifier for this rate limit event.
     * <p>
     * Generated when the rate limit event is created to ensure
     * each detected violation has a unique identifier.
     * </p>
     */
    @SerializedName("rateLimitEventId")
    private final UUID rateLimitEventId;

    /**
     * The type of rate limit event (Warning, Throttle, or Flag).
     */
    @SerializedName("eventType")
    private final RateLimitEventType eventType;

    /**
     * The user ID who triggered this rate limit event.
     */
    @SerializedName("userId")
    private final String userId;

    /**
     * The scope type where the rate limit was triggered.
     */
    @SerializedName("scopeType")
    private final ChatScopeTypeEnum scopeType;

    /**
     * The scope ID where the rate limit was triggered.
     */
    @SerializedName("scopeId")
    private final String scopeId;

    /**
     * The start timestamp of the monitoring window (ISO-8601 UTC).
     */
    @SerializedName("windowStartUtc")
    private final String windowStartUtc;

    /**
     * The end timestamp of the monitoring window (ISO-8601 UTC).
     */
    @SerializedName("windowEndUtc")
    private final String windowEndUtc;

    /**
     * The duration of the monitoring window in seconds.
     */
    @SerializedName("windowDurationSeconds")
    private final long windowDurationSeconds;

    /**
     * The number of messages sent by the user in this window.
     */
    @SerializedName("messageCount")
    private final long messageCount;

    /**
     * The threshold that was exceeded.
     */
    @SerializedName("threshold")
    private final long threshold;

    /**
     * The number of messages by which the threshold was exceeded.
     */
    @SerializedName("excessCount")
    private final long excessCount;

    /**
     * The messages per second rate for this window.
     */
    @SerializedName("messagesPerSecond")
    private final double messagesPerSecond;

    /**
     * The timestamp when this rate limit event was detected (ISO-8601 UTC).
     */
    @SerializedName("detectedAtUtc")
    private final String detectedAtUtc;

    /**
     * The identifier of the Flink task that detected this event.
     */
    @SerializedName("detectorTaskId")
    private final String detectorTaskId;

    /**
     * Additional context about the rate limit event.
     */
    @SerializedName("context")
    private final String context;

    /**
     * GSON instance for JSON serialization.
     */
    private static final Gson GSON = new Gson();

    /**
     * Constructs a new RateLimitEventEntity with all fields.
     *
     * @param rateLimitEventId       The unique identifier for this event.
     * @param eventType              The type of rate limit event.
     * @param userId                 The user ID who triggered the event.
     * @param scopeType              The scope type where triggered.
     * @param scopeId                The scope ID where triggered.
     * @param windowStartUtc         The window start timestamp.
     * @param windowEndUtc           The window end timestamp.
     * @param windowDurationSeconds  The window duration in seconds.
     * @param messageCount           The message count in the window.
     * @param threshold              The threshold that was exceeded.
     * @param excessCount            The excess message count.
     * @param messagesPerSecond      The messages per second rate.
     * @param detectedAtUtc          The detection timestamp.
     * @param detectorTaskId         The detector task identifier.
     * @param context                Additional context information.
     */
    public RateLimitEventEntity(
            UUID rateLimitEventId,
            RateLimitEventType eventType,
            String userId,
            ChatScopeTypeEnum scopeType,
            String scopeId,
            String windowStartUtc,
            String windowEndUtc,
            long windowDurationSeconds,
            long messageCount,
            long threshold,
            long excessCount,
            double messagesPerSecond,
            String detectedAtUtc,
            String detectorTaskId,
            String context) {
        this.rateLimitEventId = Objects.requireNonNull(rateLimitEventId, "rateLimitEventId must not be null");
        this.eventType = Objects.requireNonNull(eventType, "eventType must not be null");
        this.userId = Objects.requireNonNull(userId, "userId must not be null");
        this.scopeType = Objects.requireNonNull(scopeType, "scopeType must not be null");
        this.scopeId = Objects.requireNonNull(scopeId, "scopeId must not be null");
        this.windowStartUtc = Objects.requireNonNull(windowStartUtc, "windowStartUtc must not be null");
        this.windowEndUtc = Objects.requireNonNull(windowEndUtc, "windowEndUtc must not be null");
        this.windowDurationSeconds = windowDurationSeconds;
        this.messageCount = messageCount;
        this.threshold = threshold;
        this.excessCount = excessCount;
        this.messagesPerSecond = messagesPerSecond;
        this.detectedAtUtc = Objects.requireNonNull(detectedAtUtc, "detectedAtUtc must not be null");
        this.detectorTaskId = Objects.requireNonNull(detectorTaskId, "detectorTaskId must not be null");
        this.context = context; // Context can be null
    }

    /**
     * Creates a builder for constructing RateLimitEventEntity instances.
     *
     * @return A new RateLimitEventEntityBuilder.
     */
    public static RateLimitEventEntityBuilder builder() {
        return new RateLimitEventEntityBuilder();
    }

    /**
     * Serializes this RateLimitEventEntity to a JSON string for Kafka production.
     *
     * @return The JSON representation of this rate limit event.
     */
    public String toJson() {
        return GSON.toJson(this);
    }

    // Getters

    public UUID getRateLimitEventId() {
        return rateLimitEventId;
    }

    public RateLimitEventType getEventType() {
        return eventType;
    }

    public String getUserId() {
        return userId;
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

    public long getThreshold() {
        return threshold;
    }

    public long getExcessCount() {
        return excessCount;
    }

    public double getMessagesPerSecond() {
        return messagesPerSecond;
    }

    public String getDetectedAtUtc() {
        return detectedAtUtc;
    }

    public String getDetectorTaskId() {
        return detectorTaskId;
    }

    public String getContext() {
        return context;
    }

    /**
     * Gets the partition key for Kafka production.
     * <p>
     * Uses the userId as the partition key to ensure all rate limit events
     * for the same user are written to the same partition for ordered processing.
     * </p>
     *
     * @return The partition key (userId).
     */
    public String getPartitionKey() {
        return userId;
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (o == null || getClass() != o.getClass()) return false;
        RateLimitEventEntity that = (RateLimitEventEntity) o;
        return Objects.equals(rateLimitEventId, that.rateLimitEventId);
    }

    @Override
    public int hashCode() {
        return Objects.hash(rateLimitEventId);
    }

    @Override
    public String toString() {
        return "RateLimitEventEntity{" +
                "rateLimitEventId=" + rateLimitEventId +
                ", eventType=" + eventType +
                ", userId='" + userId + '\'' +
                ", scopeType=" + scopeType +
                ", scopeId='" + scopeId + '\'' +
                ", windowStartUtc='" + windowStartUtc + '\'' +
                ", windowEndUtc='" + windowEndUtc + '\'' +
                ", windowDurationSeconds=" + windowDurationSeconds +
                ", messageCount=" + messageCount +
                ", threshold=" + threshold +
                ", excessCount=" + excessCount +
                ", messagesPerSecond=" + messagesPerSecond +
                ", detectedAtUtc='" + detectedAtUtc + '\'' +
                ", detectorTaskId='" + detectorTaskId + '\'' +
                ", context='" + context + '\'' +
                '}';
    }

    /**
     * Builder class for constructing RateLimitEventEntity instances.
     */
    public static class RateLimitEventEntityBuilder {
        private UUID rateLimitEventId;
        private RateLimitEventType eventType;
        private String userId;
        private ChatScopeTypeEnum scopeType;
        private String scopeId;
        private String windowStartUtc;
        private String windowEndUtc;
        private long windowDurationSeconds;
        private long messageCount;
        private long threshold;
        private long excessCount;
        private double messagesPerSecond;
        private String detectedAtUtc;
        private String detectorTaskId;
        private String context;

        public RateLimitEventEntityBuilder rateLimitEventId(UUID rateLimitEventId) {
            this.rateLimitEventId = rateLimitEventId;
            return this;
        }

        public RateLimitEventEntityBuilder eventType(RateLimitEventType eventType) {
            this.eventType = eventType;
            return this;
        }

        public RateLimitEventEntityBuilder userId(String userId) {
            this.userId = userId;
            return this;
        }

        public RateLimitEventEntityBuilder scopeType(ChatScopeTypeEnum scopeType) {
            this.scopeType = scopeType;
            return this;
        }

        public RateLimitEventEntityBuilder scopeId(String scopeId) {
            this.scopeId = scopeId;
            return this;
        }

        public RateLimitEventEntityBuilder windowStartUtc(String windowStartUtc) {
            this.windowStartUtc = windowStartUtc;
            return this;
        }

        public RateLimitEventEntityBuilder windowEndUtc(String windowEndUtc) {
            this.windowEndUtc = windowEndUtc;
            return this;
        }

        public RateLimitEventEntityBuilder windowDurationSeconds(long windowDurationSeconds) {
            this.windowDurationSeconds = windowDurationSeconds;
            return this;
        }

        public RateLimitEventEntityBuilder messageCount(long messageCount) {
            this.messageCount = messageCount;
            return this;
        }

        public RateLimitEventEntityBuilder threshold(long threshold) {
            this.threshold = threshold;
            return this;
        }

        public RateLimitEventEntityBuilder excessCount(long excessCount) {
            this.excessCount = excessCount;
            return this;
        }

        public RateLimitEventEntityBuilder messagesPerSecond(double messagesPerSecond) {
            this.messagesPerSecond = messagesPerSecond;
            return this;
        }

        public RateLimitEventEntityBuilder detectedAtUtc(String detectedAtUtc) {
            this.detectedAtUtc = detectedAtUtc;
            return this;
        }

        public RateLimitEventEntityBuilder detectorTaskId(String detectorTaskId) {
            this.detectorTaskId = detectorTaskId;
            return this;
        }

        public RateLimitEventEntityBuilder context(String context) {
            this.context = context;
            return this;
        }

        /**
         * Builds the RateLimitEventEntity with the configured values.
         *
         * @return A new RateLimitEventEntity instance.
         */
        public RateLimitEventEntity build() {
            return new RateLimitEventEntity(
                    rateLimitEventId,
                    eventType,
                    userId,
                    scopeType,
                    scopeId,
                    windowStartUtc,
                    windowEndUtc,
                    windowDurationSeconds,
                    messageCount,
                    threshold,
                    excessCount,
                    messagesPerSecond,
                    detectedAtUtc,
                    detectorTaskId,
                    context
            );
        }
    }
}
