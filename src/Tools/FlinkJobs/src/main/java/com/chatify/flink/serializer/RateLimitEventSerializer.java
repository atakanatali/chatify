package com.chatify.flink.serializer;

import com.chatify.flink.model.RateLimitEventEntity;
import org.apache.flink.api.common.serialization.SerializationSchema;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.nio.charset.StandardCharsets;

/**
 * Serialization schema for converting {@link RateLimitEventEntity} instances to Kafka messages.
 * <p>
 * This class implements Flink's {@link SerializationSchema} to serialize
 * {@link RateLimitEventEntity} objects to JSON for production to the
 * {@code rate-limit-events} Kafka topic.
 * </p>
 * <p>
 * <b>Thread Safety:</b> This serializer is thread-safe. Each {@link RateLimitEventEntity}
 * serializes itself independently using the {@link RateLimitEventEntity#toJson()} method.
 * </p>
 *
 * @see RateLimitEventEntity
 * @see org.apache.flink.streaming.connectors.kafka.FlinkKafkaProducer
 * @since 1.0.0
 */
public class RateLimitEventSerializer implements SerializationSchema<RateLimitEventEntity> {

    private static final long serialVersionUID = 1L;

    private static final Logger LOG = LoggerFactory.getLogger(RateLimitEventSerializer.class);

    /**
     * The topic name to which rate limit events are produced.
     */
    private final String topic;

    /**
     * Constructs a new RateLimitEventSerializer for the specified topic.
     *
     * @param topic The Kafka topic name for rate limit events.
     */
    public RateLimitEventSerializer(String topic) {
        this.topic = topic;
    }

    /**
     * Constructs a new RateLimitEventSerializer with the default topic name.
     */
    public RateLimitEventSerializer() {
        this("rate-limit-events");
    }

    /**
     * Serializes a {@link RateLimitEventEntity} to a byte array for Kafka production.
     * <p>
     * The output is a UTF-8 encoded JSON string matching the schema defined by
     * {@link RateLimitEventEntity}.
     * </p>
     *
     * @param rateLimitEvent The rate limit event to serialize.
     * @return The byte array containing the JSON representation, or {@code null} if serialization fails.
     */
    @Override
    public byte[] serialize(RateLimitEventEntity rateLimitEvent) {
        if (rateLimitEvent == null) {
            LOG.warn("Attempted to serialize null rate limit event, skipping");
            return null;
        }

        try {
            String json = rateLimitEvent.toJson();
            return json.getBytes(StandardCharsets.UTF_8);
        } catch (Exception e) {
            LOG.error("Failed to serialize rate limit event: {}", e.getMessage(), e);
            // TODO: Send to dead-letter queue for analysis instead of silently dropping
            return null;
        }
    }

    /**
     * Gets the topic name for this serializer.
     *
     * @return The Kafka topic name.
     */
    public String getTopic() {
        return topic;
    }
}
