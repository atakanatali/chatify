package com.chatify.flink.serializer;

import com.chatify.flink.model.ChatEventEntity;
import com.google.gson.Gson;
import com.google.gson.JsonSyntaxException;
import org.apache.flink.api.common.serialization.DeserializationSchema;
import org.apache.flink.api.common.typeinfo.TypeInformation;
import org.apache.flink.api.java.typeutils.TypeExtractor;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.IOException;
import java.nio.charset.StandardCharsets;

/**
 * Deserialization schema for converting Kafka messages to {@link ChatEventEntity} instances.
 * <p>
 * This class implements Flink's {@link DeserializationSchema} to deserialize JSON messages
 * from the {@code chat-events} Kafka topic into strongly-typed {@link ChatEventEntity} objects.
 * </p>
 * <p>
 * <b>Thread Safety:</b> This deserializer is thread-safe and can be shared across
 * multiple Flink operator instances.
 * </p>
 * <p>
 * <b>Error Handling:</b> Deserialization failures are logged and result in returning
 * {@code null}, which causes Flink to skip the problematic message. In production,
 * consider implementing a dead-letter queue pattern for failed messages.
 * </p>
 *
 * @see ChatEventEntity
 * @see org.apache.flink.streaming.connectors.kafka.FlinkKafkaConsumer
 * @since 1.0.0
 */
public class ChatEventDeserializer implements DeserializationSchema<ChatEventEntity> {

    private static final long serialVersionUID = 1L;

    private static final Logger LOG = LoggerFactory.getLogger(ChatEventDeserializer.class);

    /**
     * GSON instance for JSON deserialization.
     * <p>
     * GSON is thread-safe and can be reused across deserialization calls.
     * </p>
     */
    private static final Gson GSON = new Gson();

    /**
     * The type information for {@link ChatEventEntity}, used by Flink for serialization.
     */
    private static final TypeInformation<ChatEventEntity> TYPE_INFO = TypeExtractor.getForClass(ChatEventEntity.class);

    /**
     * Deserializes a byte array to a {@link ChatEventEntity}.
     * <p>
     * The input is expected to be a UTF-8 encoded JSON string matching the
     * schema defined by {@code ChatEventDto} in the Chatify.Chat.Application layer.
     * </p>
     *
     * @param message The byte array containing the JSON message.
     * @return The deserialized {@link ChatEventEntity}, or {@code null} if deserialization fails.
     */
    @Override
    public ChatEventEntity deserialize(byte[] message) {
        if (message == null || message.length == 0) {
            LOG.warn("Received empty or null message, skipping");
            return null;
        }

        try {
            String json = new String(message, StandardCharsets.UTF_8);
            return GSON.fromJson(json, ChatEventEntity.class);
        } catch (JsonSyntaxException e) {
            LOG.error("Failed to deserialize chat event: {}", e.getMessage(), e);
            // TODO: Send to dead-letter queue for analysis instead of silently dropping
            return null;
        } catch (Exception e) {
            LOG.error("Unexpected error deserializing chat event: {}", e.getMessage(), e);
            // TODO: Send to dead-letter queue for analysis instead of silently dropping
            return null;
        }
    }

    /**
     * Indicates whether this deserializer is the end of the stream.
     * <p>
     * This method is not used in Kafka consumption (which is infinite), but must
     * be implemented as part of the {@link DeserializationSchema} interface.
     * </p>
     *
     * @param nextElement The next element to check.
     * @return Always returns {@code false} for Kafka streams.
     */
    @Override
    public boolean isEndOfStream(ChatEventEntity nextElement) {
        // Kafka streams are infinite
        return false;
    }

    /**
     * Gets the type information for {@link ChatEventEntity}.
     * <p>
     * Flink uses this information for serialization and type handling in
     * the streaming pipeline.
     * </p>
     *
     * @return The type information for {@link ChatEventEntity}.
     */
    @Override
    public TypeInformation<ChatEventEntity> getProducedType() {
        return TYPE_INFO;
    }
}
