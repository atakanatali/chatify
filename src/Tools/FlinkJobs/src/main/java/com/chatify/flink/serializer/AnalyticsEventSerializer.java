package com.chatify.flink.serializer;

import com.chatify.flink.model.AnalyticsEventEntity;
import org.apache.flink.api.common.serialization.SerializationSchema;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.nio.charset.StandardCharsets;

/**
 * Serialization schema for converting {@link AnalyticsEventEntity} instances to Kafka messages.
 * <p>
 * This class implements Flink's {@link SerializationSchema} to serialize
 * {@link AnalyticsEventEntity} objects to JSON for production to the
 * {@code analytics-events} Kafka topic.
 * </p>
 * <p>
 * <b>Thread Safety:</b> This serializer is thread-safe. Each {@link AnalyticsEventEntity}
 * serializes itself independently using the {@link AnalyticsEventEntity#toJson()} method.
 * </p>
 *
 * @see AnalyticsEventEntity
 * @see org.apache.flink.streaming.connectors.kafka.FlinkKafkaProducer
 * @since 1.0.0
 */
public class AnalyticsEventSerializer implements SerializationSchema<AnalyticsEventEntity> {

    private static final long serialVersionUID = 1L;

    private static final Logger LOG = LoggerFactory.getLogger(AnalyticsEventSerializer.class);

    /**
     * The topic name to which analytics events are produced.
     */
    private final String topic;

    /**
     * Constructs a new AnalyticsEventSerializer for the specified topic.
     *
     * @param topic The Kafka topic name for analytics events.
     */
    public AnalyticsEventSerializer(String topic) {
        this.topic = topic;
    }

    /**
     * Constructs a new AnalyticsEventSerializer with the default topic name.
     */
    public AnalyticsEventSerializer() {
        this("analytics-events");
    }

    /**
     * Serializes an {@link AnalyticsEventEntity} to a byte array for Kafka production.
     * <p>
     * The output is a UTF-8 encoded JSON string matching the schema defined by
     * {@link AnalyticsEventEntity}.
     * </p>
     *
     * @param analyticsEvent The analytics event to serialize.
     * @return The byte array containing the JSON representation, or {@code null} if serialization fails.
     */
    @Override
    public byte[] serialize(AnalyticsEventEntity analyticsEvent) {
        if (analyticsEvent == null) {
            LOG.warn("Attempted to serialize null analytics event, skipping");
            return null;
        }

        try {
            String json = analyticsEvent.toJson();
            return json.getBytes(StandardCharsets.UTF_8);
        } catch (Exception e) {
            LOG.error("Failed to serialize analytics event: {}", e.getMessage(), e);
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
