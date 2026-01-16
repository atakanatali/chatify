package com.chatify.flink.processor;

import com.chatify.flink.config.FlinkJobConfiguration;
import com.chatify.flink.model.AnalyticsEventEntity;
import com.chatify.flink.model.ChatEventEntity;
import com.chatify.flink.model.ChatScopeTypeEnum;
import com.chatify.flink.model.RateLimitEventEntity;
import com.chatify.flink.serializer.ChatEventDeserializer;
import org.apache.flink.api.common.eventtime.WatermarkStrategy;
import org.apache.flink.api.common.functions.AggregateFunction;
import org.apache.flink.api.common.functions.FlatMapFunction;
import org.apache.flink.api.common.functions.MapFunction;
import org.apache.flink.streaming.api.datastream.DataStream;
import org.apache.flink.streaming.api.environment.StreamExecutionEnvironment;
import org.apache.flink.streaming.api.windowing.assigners.SlidingEventTimeWindows;
import org.apache.flink.streaming.api.windowing.assigners.TumblingEventTimeWindows;
import org.apache.flink.streaming.api.windowing.time.Time;
import org.apache.flink.streaming.connectors.kafka.FlinkKafkaConsumer;
import org.apache.flink.streaming.connectors.kafka.FlinkKafkaProducer;
import org.apache.flink.streaming.connectors.kafka.KafkaSerializationSchema;
import org.apache.flink.util.Collector;
import org.apache.kafka.clients.consumer.ConsumerConfig;
import org.apache.kafka.clients.producer.ProducerRecord;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.List;
import java.util.Properties;
import java.util.UUID;

/**
 * Main Flink streaming job for processing Chatify chat events.
 * <p>
 * This job consumes chat events from the {@code chat-events} Kafka topic,
 * performs real-time analytics aggregation and rate limit detection, and
 * produces derived events to {@code analytics-events} and {@code rate-limit-events} topics.
 * </p>
 * <p>
 * <b>Processing Pipeline:</b>
 * <pre>
 * chat-events (Kafka)
 *     ↓
 * ChatEventDeserializer
 *     ↓
 * [Fork]
 * ├─→ Analytics Aggregation → AnalyticsEventSerializer → analytics-events (Kafka)
 * └─→ Rate Limit Detection → RateLimitEventSerializer → rate-limit-events (Kafka)
 * </pre>
 * </p>
 * <p>
 * <b>Analytics Aggregation:</b>
 * <ul>
 *   <li>Aggregates messages by composite scope ID (scopeType:scopeId)</li>
 *   <li>Computes tumbling window statistics (message count, active users, avg length)</li>
 *   <li>Produces one analytics event per window per scope</li>
 * </ul>
 * </p>
 * <p>
 * <b>Rate Limit Detection:</b>
 * <ul>
 *   <li>Aggregates messages by user ID across all scopes</li>
 *   <li>Computes sliding window statistics for high-frequency detection</li>
 *   <li>Produces rate limit events when thresholds are exceeded</li>
 * </ul>
 * </p>
 * <p>
 * <b>Checkpointing:</b> The job enables exactly-once processing guarantees with
 * checkpointing configured via environment variables. Checkpoints are stored
 * in the Flink JobManager's state backend (TODO: Configure external state backend for production).
 * </p>
 * <p>
 * <b>TODO:</b>
 * <ul>
 *   <li>Configure external state backend (e.g., RocksDB) for larger state</li>
 *   <li>Implement dead-letter queue for failed messages</li>
 *   <li>Add metrics exposure via Prometheus/Flink metrics</li>
 *   <li>Implement dynamic threshold configuration via config topic</li>
 *   <li>Track unique users and senders properly in analytics</li>
 *   <li>Implement proper window start/end time tracking</li>
 * </ul>
 * </p>
 *
 * @see ChatEventEntity
 * @see AnalyticsEventEntity
 * @see RateLimitEventEntity
 * @see FlinkJobConfiguration
 * @since 1.0.0
 */
public class ChatEventProcessorJob {

    private static final Logger LOG = LoggerFactory.getLogger(ChatEventProcessorJob.class);
    private static final DateTimeFormatter ISO_FORMATTER = DateTimeFormatter
            .ofPattern("yyyy-MM-dd'T'HH:mm:ss'Z'")
            .withZone(ZoneOffset.UTC);

    /**
     * Main entry point for the Flink job.
     *
     * @param args Command line arguments (not currently used; configuration via env vars).
     * @throws Exception if the job fails to execute.
     */
    public static void main(String[] args) throws Exception {
        // Log and validate configuration
        FlinkJobConfiguration.logConfiguration();
        FlinkJobConfiguration.validateConfiguration();

        // Create execution environment
        final StreamExecutionEnvironment env = StreamExecutionEnvironment.getExecutionEnvironment();
        env.setParallelism(FlinkJobConfiguration.JOB_PARALLELISM);

        // Configure checkpointing for exactly-once semantics
        configureCheckpointing(env);

        // Create Kafka source
        FlinkKafkaConsumer<ChatEventEntity> kafkaSource = createKafkaSource();
        DataStream<ChatEventEntity> chatEvents = env
                .addSource(kafkaSource, "Kafka Chat Events Source")
                .name("Kafka Chat Events Source")
                .uid("kafka-chat-events-source");

        // Assign watermarks based on event time
        DataStream<ChatEventEntity> watermarkedEvents = chatEvents
                .assignTimestampsAndWatermarks(
                        WatermarkStrategy.<ChatEventEntity>forBoundedOutOfOrderness(
                                        Time.seconds(5)
                                )
                                .withTimestampAssigner((event, timestamp) -> event.getCreatedAtAsInstant().toEpochMilli())
                                .withIdleness(Duration.ofSeconds(60))
                )
                .name("Assign Timestamps and Watermarks")
                .uid("assign-timestamps-watermarks");

        // Branch 1: Analytics Aggregation (by scope)
        DataStream<AnalyticsEventEntity> analyticsEvents = processAnalytics(watermarkedEvents);

        // Branch 2: Rate Limit Detection (by user)
        DataStream<RateLimitEventEntity> rateLimitEvents = processRateLimits(watermarkedEvents);

        // Sink analytics events to Kafka
        FlinkKafkaProducer<AnalyticsEventEntity> analyticsProducer = createAnalyticsProducer();
        analyticsEvents
                .sinkTo(analyticsProducer)
                .name("Kafka Analytics Events Sink")
                .uid("kafka-analytics-sink");

        // Sink rate limit events to Kafka
        FlinkKafkaProducer<RateLimitEventEntity> rateLimitProducer = createRateLimitProducer();
        rateLimitEvents
                .sinkTo(rateLimitProducer)
                .name("Kafka Rate Limit Events Sink")
                .uid("kafka-rate-limit-sink");

        // Execute the job
        LOG.info("Starting Chatify Chat Event Processor job...");
        env.execute("Chatify Chat Event Processor");
    }

    /**
     * Configures checkpointing for exactly-once processing semantics.
     *
     * @param env The Flink execution environment.
     */
    private static void configureCheckpointing(StreamExecutionEnvironment env) {
        env.enableCheckpointing(FlinkJobConfiguration.CHECKPOINT_INTERVAL_MS);
        env.getCheckpointConfig().setMinPauseBetweenCheckpoints(
                FlinkJobConfiguration.CHECKPOINT_MIN_PAUSE_MS
        );
        env.getCheckpointConfig().setCheckpointTimeout(
                FlinkJobConfiguration.CHECKPOINT_TIMEOUT_MS
        );
        env.getCheckpointConfig().setMaxConcurrentCheckpoints(
                FlinkJobConfiguration.CHECKPOINT_MAX_CONCURRENT
        );
        // TODO: Configure external state backend for production (RocksDB + HDFS/S3)
        // env.setStateBackend(new EmbeddedRocksDBStateBackend());
        // env.getCheckpointConfig().setCheckpointStorage("hdfs:///flink/checkpoints");
    }

    /**
     * Creates the Kafka consumer for chat events.
     *
     * @return A configured FlinkKafkaConsumer for ChatEventEntity.
     */
    private static FlinkKafkaConsumer<ChatEventEntity> createKafkaSource() {
        Properties properties = new Properties();
        properties.setProperty(ConsumerConfig.BOOTSTRAP_SERVERS_CONFIG,
                FlinkJobConfiguration.KAFKA_BOOTSTRAP_SERVERS);
        properties.setProperty(ConsumerConfig.GROUP_ID_CONFIG,
                FlinkJobConfiguration.KAFKA_CONSUMER_GROUP_ID);
        properties.setProperty(ConsumerConfig.AUTO_OFFSET_RESET_CONFIG, "earliest");

        FlinkKafkaConsumer<ChatEventEntity> consumer = new FlinkKafkaConsumer<>(
                FlinkJobConfiguration.KAFKA_SOURCE_TOPIC,
                new ChatEventDeserializer(),
                properties
        );
        consumer.setStartFromEarliest();
        return consumer;
    }

    /**
     * Creates the Kafka producer for analytics events.
     *
     * @return A configured FlinkKafkaProducer for AnalyticsEventEntity.
     */
    private static FlinkKafkaProducer<AnalyticsEventEntity> createAnalyticsProducer() {
        Properties properties = new Properties();
        properties.setProperty("bootstrap.servers", FlinkJobConfiguration.KAFKA_BOOTSTRAP_SERVERS);

        return new FlinkKafkaProducer<>(
                new AnalyticsKafkaSerializationSchema(
                        FlinkJobConfiguration.KAFKA_ANALYTICS_TOPIC
                ),
                FlinkKafkaProducer.Semantic.EXACTLY_ONCE,
                properties
        );
    }

    /**
     * Creates the Kafka producer for rate limit events.
     *
     * @return A configured FlinkKafkaProducer for RateLimitEventEntity.
     */
    private static FlinkKafkaProducer<RateLimitEventEntity> createRateLimitProducer() {
        Properties properties = new Properties();
        properties.setProperty("bootstrap.servers", FlinkJobConfiguration.KAFKA_BOOTSTRAP_SERVERS);

        return new FlinkKafkaProducer<>(
                new RateLimitKafkaSerializationSchema(
                        FlinkJobConfiguration.KAFKA_RATE_LIMIT_TOPIC
                ),
                FlinkKafkaProducer.Semantic.EXACTLY_ONCE,
                properties
        );
    }

    /**
     * Processes chat events to generate analytics aggregations.
     * <p>
     * Aggregates messages by composite scope ID over tumbling time windows,
     * computing statistics like message count, active users, and average message length.
     * </p>
     *
     * @param watermarkedEvents The input stream of chat events with watermarks.
     * @return A stream of analytics events.
     */
    private static DataStream<AnalyticsEventEntity> processAnalytics(
            DataStream<ChatEventEntity> watermarkedEvents) {

        return watermarkedEvents
                .keyBy(ChatEventEntity::getCompositeScopeId)
                .window(TumblingEventTimeWindows.of(
                        Time.seconds(FlinkJobConfiguration.ANALYTICS_WINDOW_SIZE_SECONDS)
                ))
                .aggregate(new AnalyticsAggregateFunction(), new AnalyticsProcessWindowFunction())
                .name("Analytics Aggregation")
                .uid("analytics-aggregation");
    }

    /**
     * Processes chat events to detect rate limit violations.
     * <p>
     * Aggregates messages by user ID over sliding time windows,
     * detecting when users exceed configured thresholds.
     * </p>
     *
     * @param watermarkedEvents The input stream of chat events with watermarks.
     * @return A stream of rate limit events.
     */
    private static DataStream<RateLimitEventEntity> processRateLimits(
            DataStream<ChatEventEntity> watermarkedEvents) {

        return watermarkedEvents
                .keyBy(ChatEventEntity::getSenderId)
                .window(SlidingEventTimeWindows.of(
                        Time.seconds(FlinkJobConfiguration.RATE_LIMIT_WINDOW_SIZE_SECONDS),
                        Time.seconds(10) // Slide every 10 seconds for frequent detection
                ))
                .aggregate(new RateLimitAggregateFunction(), new RateLimitProcessWindowFunction())
                .flatMap(new RateLimitEventMapper())
                .name("Rate Limit Detection")
                .uid("rate-limit-detection");
    }

    // ========================================
    // Analytics Aggregation Components
    // ========================================

    /**
     * Aggregate function for computing analytics per scope window.
     */
    private static class AnalyticsAggregateFunction implements
            AggregateFunction<ChatEventEntity, AnalyticsAccumulator, AnalyticsWindowResult> {

        @Override
        public AnalyticsAccumulator createAccumulator() {
            return new AnalyticsAccumulator();
        }

        @Override
        public AnalyticsAccumulator add(ChatEventEntity event, AnalyticsAccumulator accumulator) {
            accumulator.messageCount++;
            accumulator.totalCharacterCount += event.getText().length();
            if (accumulator.firstEvent == null) {
                accumulator.firstEvent = event;
            }
            accumulator.lastEvent = event;
            // Track unique users using a set (simplified; consider HyperLogLog for production)
            accumulator.uniqueUsers.add(event.getSenderId());
            return accumulator;
        }

        @Override
        public AnalyticsWindowResult getResult(AnalyticsAccumulator accumulator) {
            return new AnalyticsWindowResult(
                    accumulator.firstEvent.getScopeType(),
                    accumulator.firstEvent.getScopeId(),
                    accumulator.messageCount,
                    accumulator.uniqueUsers.size(),
                    accumulator.totalCharacterCount,
                    (double) accumulator.totalCharacterCount / accumulator.messageCount,
                    accumulator.firstEvent.getCreatedAtAsInstant(),
                    accumulator.lastEvent.getCreatedAtAsInstant()
            );
        }

        @Override
        public AnalyticsAccumulator merge(AnalyticsAccumulator a, AnalyticsAccumulator b) {
            a.messageCount += b.messageCount;
            a.totalCharacterCount += b.totalCharacterCount;
            a.uniqueUsers.addAll(b.uniqueUsers);
            if (b.firstEvent != null && (a.firstEvent == null ||
                    b.firstEvent.getCreatedAtAsInstant().isBefore(a.firstEvent.getCreatedAtAsInstant()))) {
                a.firstEvent = b.firstEvent;
            }
            if (b.lastEvent != null && (a.lastEvent == null ||
                    b.lastEvent.getCreatedAtAsInstant().isAfter(a.lastEvent.getCreatedAtAsInstant()))) {
                a.lastEvent = b.lastEvent;
            }
            return a;
        }
    }

    /**
     * Accumulator for analytics aggregation.
     */
    private static class AnalyticsAccumulator {
        long messageCount = 0;
        long totalCharacterCount = 0;
        java.util.Set<String> uniqueUsers = new java.util.HashSet<>();
        ChatEventEntity firstEvent = null;
        ChatEventEntity lastEvent = null;
    }

    /**
     * Result type for analytics window aggregation.
     */
    private static class AnalyticsWindowResult {
        final ChatScopeTypeEnum scopeType;
        final String scopeId;
        final long messageCount;
        final long activeUserCount;
        final long totalCharacterCount;
        final double averageMessageLength;
        final Instant windowStart;
        final Instant windowEnd;

        AnalyticsWindowResult(ChatScopeTypeEnum scopeType, String scopeId,
                             long messageCount, long activeUserCount,
                             long totalCharacterCount, double averageMessageLength,
                             Instant windowStart, Instant windowEnd) {
            this.scopeType = scopeType;
            this.scopeId = scopeId;
            this.messageCount = messageCount;
            this.activeUserCount = activeUserCount;
            this.totalCharacterCount = totalCharacterCount;
            this.averageMessageLength = averageMessageLength;
            this.windowStart = windowStart;
            this.windowEnd = windowEnd;
        }
    }

    /**
     * Process window function to convert aggregation results to AnalyticsEventEntity.
     */
    private static class AnalyticsProcessWindowFunction implements
            org.apache.flink.streaming.api.functions.windowing.ProcessWindowFunction<
                    AnalyticsWindowResult, AnalyticsEventEntity, String, org.apache.flink.streaming.api.windowing.windows.TimeWindow> {

        @Override
        public void process(String key,
                           org.apache.flink.streaming.api.functions.windowing.ProcessWindowFunction<AnalyticsWindowResult, AnalyticsEventEntity, String, org.apache.flink.streaming.api.windowing.windows.TimeWindow>.Context ctx,
                           Iterable<AnalyticsWindowResult> results,
                           Collector<AnalyticsEventEntity> out) {
            AnalyticsWindowResult result = results.iterator().next();
            String now = ISO_FORMATTER.format(Instant.now());

            AnalyticsEventEntity event = AnalyticsEventEntity.builder()
                    .analyticsId(UUID.randomUUID())
                    .scopeType(result.scopeType)
                    .scopeId(result.scopeId)
                    .windowStartUtc(ISO_FORMATTER.format(result.windowStart))
                    .windowEndUtc(ISO_FORMATTER.format(result.windowEnd))
                    .windowDurationSeconds(FlinkJobConfiguration.ANALYTICS_WINDOW_SIZE_SECONDS)
                    .messageCount(result.messageCount)
                    .activeUserCount(result.activeUserCount)
                    .uniqueSenderCount(result.activeUserCount) // Same as active users for now
                    .totalCharacterCount(result.totalCharacterCount)
                    .averageMessageLength(result.averageMessageLength)
                    .computedAtUtc(now)
                    .computeTaskId("chatify-flink-processor")
                    .build();

            out.collect(event);
        }
    }

    // ========================================
    // Rate Limit Detection Components
    // ========================================

    /**
     * Aggregate function for tracking user message frequency.
     */
    private static class RateLimitAggregateFunction implements
            AggregateFunction<ChatEventEntity, RateLimitAccumulator, RateLimitWindowResult> {

        @Override
        public RateLimitAccumulator createAccumulator() {
            return new RateLimitAccumulator();
        }

        @Override
        public RateLimitAccumulator add(ChatEventEntity event, RateLimitAccumulator accumulator) {
            accumulator.messageCount++;
            if (accumulator.firstEvent == null) {
                accumulator.firstEvent = event;
            }
            accumulator.lastEvent = event;
            // Track all scopes the user posted in
            accumulator.scopes.add(event.getCompositeScopeId());
            return accumulator;
        }

        @Override
        public RateLimitWindowResult getResult(RateLimitAccumulator accumulator) {
            return new RateLimitWindowResult(
                    accumulator.firstEvent.getSenderId(),
                    accumulator.messageCount,
                    accumulator.scopes,
                    accumulator.firstEvent.getCreatedAtAsInstant(),
                    accumulator.lastEvent.getCreatedAtAsInstant()
            );
        }

        @Override
        public RateLimitAccumulator merge(RateLimitAccumulator a, RateLimitAccumulator b) {
            a.messageCount += b.messageCount;
            a.scopes.addAll(b.scopes);
            if (b.firstEvent != null && (a.firstEvent == null ||
                    b.firstEvent.getCreatedAtAsInstant().isBefore(a.firstEvent.getCreatedAtAsInstant()))) {
                a.firstEvent = b.firstEvent;
            }
            if (b.lastEvent != null && (a.lastEvent == null ||
                    b.lastEvent.getCreatedAtAsInstant().isAfter(a.lastEvent.getCreatedAtAsInstant()))) {
                a.lastEvent = b.lastEvent;
            }
            return a;
        }
    }

    /**
     * Accumulator for rate limit aggregation.
     */
    private static class RateLimitAccumulator {
        long messageCount = 0;
        java.util.Set<String> scopes = new java.util.HashSet<>();
        ChatEventEntity firstEvent = null;
        ChatEventEntity lastEvent = null;
    }

    /**
     * Result type for rate limit window aggregation.
     */
    private static class RateLimitWindowResult {
        final String userId;
        final long messageCount;
        final java.util.Set<String> scopes;
        final Instant windowStart;
        final Instant windowEnd;

        RateLimitWindowResult(String userId, long messageCount,
                             java.util.Set<String> scopes,
                             Instant windowStart, Instant windowEnd) {
            this.userId = userId;
            this.messageCount = messageCount;
            this.scopes = scopes;
            this.windowStart = windowStart;
            this.windowEnd = windowEnd;
        }
    }

    /**
     * Process window function for rate limit results.
     */
    private static class RateLimitProcessWindowFunction implements
            org.apache.flink.streaming.api.functions.windowing.ProcessWindowFunction<
                    RateLimitWindowResult, RateLimitWindowResult, String, org.apache.flink.streaming.api.windowing.windows.TimeWindow> {

        @Override
        public void process(String key,
                           org.apache.flink.streaming.api.functions.windowing.ProcessWindowFunction<RateLimitWindowResult, RateLimitWindowResult, String, org.apache.flink.streaming.api.windowing.windows.TimeWindow>.Context ctx,
                           Iterable<RateLimitWindowResult> results,
                           Collector<RateLimitWindowResult> out) {
            out.collect(results.iterator().next());
        }
    }

    /**
     * FlatMap function to convert rate limit results to rate limit events based on thresholds.
     */
    private static class RateLimitEventMapper implements FlatMapFunction<RateLimitWindowResult, RateLimitEventEntity> {

        @Override
        public void flatMap(RateLimitWindowResult result, Collector<RateLimitEventEntity> out) {
            List<RateLimitEventEntity> events = new ArrayList<>();
            String now = ISO_FORMATTER.format(Instant.now());
            long windowDuration = FlinkJobConfiguration.RATE_LIMIT_WINDOW_SIZE_SECONDS;
            double messagesPerSecond = (double) result.messageCount / windowDuration;

            // Get the first scope for context (simplified; in production, handle multiple scopes)
            String firstScope = result.scopes.isEmpty() ? "unknown" : result.scopes.iterator().next();
            String[] parts = firstScope.split(":", 2);
            ChatScopeTypeEnum scopeType = parts.length == 2 ?
                    ChatScopeTypeEnum.fromValue(parts[0]) : ChatScopeTypeEnum.CHANNEL;
            String scopeId = parts.length == 2 ? parts[1] : firstScope;

            // Check thresholds and generate events
            if (result.messageCount >= FlinkJobConfiguration.RATE_LIMIT_FLAG_THRESHOLD) {
                events.add(RateLimitEventEntity.builder()
                        .rateLimitEventId(UUID.randomUUID())
                        .eventType(RateLimitEventEntity.RateLimitEventType.FLAG)
                        .userId(result.userId)
                        .scopeType(scopeType)
                        .scopeId(scopeId)
                        .windowStartUtc(ISO_FORMATTER.format(result.windowStart))
                        .windowEndUtc(ISO_FORMATTER.format(result.windowEnd))
                        .windowDurationSeconds(windowDuration)
                        .messageCount(result.messageCount)
                        .threshold(FlinkJobConfiguration.RATE_LIMIT_FLAG_THRESHOLD)
                        .excessCount(result.messageCount - FlinkJobConfiguration.RATE_LIMIT_FLAG_THRESHOLD)
                        .messagesPerSecond(messagesPerSecond)
                        .detectedAtUtc(now)
                        .detectorTaskId("chatify-flink-processor")
                        .context("User exceeded flag threshold")
                        .build());
            }

            if (result.messageCount >= FlinkJobConfiguration.RATE_LIMIT_THROTTLE_THRESHOLD) {
                events.add(RateLimitEventEntity.builder()
                        .rateLimitEventId(UUID.randomUUID())
                        .eventType(RateLimitEventEntity.RateLimitEventType.THROTTLE)
                        .userId(result.userId)
                        .scopeType(scopeType)
                        .scopeId(scopeId)
                        .windowStartUtc(ISO_FORMATTER.format(result.windowStart))
                        .windowEndUtc(ISO_FORMATTER.format(result.windowEnd))
                        .windowDurationSeconds(windowDuration)
                        .messageCount(result.messageCount)
                        .threshold(FlinkJobConfiguration.RATE_LIMIT_THROTTLE_THRESHOLD)
                        .excessCount(result.messageCount - FlinkJobConfiguration.RATE_LIMIT_THROTTLE_THRESHOLD)
                        .messagesPerSecond(messagesPerSecond)
                        .detectedAtUtc(now)
                        .detectorTaskId("chatify-flink-processor")
                        .context("User exceeded throttle threshold")
                        .build());
            }

            if (result.messageCount >= FlinkJobConfiguration.RATE_LIMIT_WARNING_THRESHOLD) {
                events.add(RateLimitEventEntity.builder()
                        .rateLimitEventId(UUID.randomUUID())
                        .eventType(RateLimitEventEntity.RateLimitEventType.WARNING)
                        .userId(result.userId)
                        .scopeType(scopeType)
                        .scopeId(scopeId)
                        .windowStartUtc(ISO_FORMATTER.format(result.windowStart))
                        .windowEndUtc(ISO_FORMATTER.format(result.windowEnd))
                        .windowDurationSeconds(windowDuration)
                        .messageCount(result.messageCount)
                        .threshold(FlinkJobConfiguration.RATE_LIMIT_WARNING_THRESHOLD)
                        .excessCount(result.messageCount - FlinkJobConfiguration.RATE_LIMIT_WARNING_THRESHOLD)
                        .messagesPerSecond(messagesPerSecond)
                        .detectedAtUtc(now)
                        .detectorTaskId("chatify-flink-processor")
                        .context("User exceeded warning threshold")
                        .build());
            }

            // Emit all generated events
            for (RateLimitEventEntity event : events) {
                out.collect(event);
            }
        }
    }

    // ========================================
    // Kafka Serialization Schemas
    // ========================================

    /**
     * Kafka serialization schema for analytics events with topic routing.
     */
    private static class AnalyticsKafkaSerializationSchema
            implements KafkaSerializationSchema<AnalyticsEventEntity> {

        private static final long serialVersionUID = 1L;
        private final String topic;

        AnalyticsKafkaSerializationSchema(String topic) {
            this.topic = topic;
        }

        @Override
        public ProducerRecord<byte[], byte[]> serialize(
                AnalyticsEventEntity event, Long timestamp) {

            try {
                byte[] value = event.toJson().getBytes();
                byte[] key = event.getPartitionKey().getBytes();
                return new ProducerRecord<>(topic, key, value);
            } catch (Exception e) {
                LOG.error("Failed to serialize analytics event: {}", e.getMessage(), e);
                return null;
            }
        }
    }

    /**
     * Kafka serialization schema for rate limit events with topic routing.
     */
    private static class RateLimitKafkaSerializationSchema
            implements KafkaSerializationSchema<RateLimitEventEntity> {

        private static final long serialVersionUID = 1L;
        private final String topic;

        RateLimitKafkaSerializationSchema(String topic) {
            this.topic = topic;
        }

        @Override
        public ProducerRecord<byte[], byte[]> serialize(
                RateLimitEventEntity event, Long timestamp) {

            try {
                byte[] value = event.toJson().getBytes();
                byte[] key = event.getPartitionKey().getBytes();
                return new ProducerRecord<>(topic, key, value);
            } catch (Exception e) {
                LOG.error("Failed to serialize rate limit event: {}", e.getMessage(), e);
                return null;
            }
        }
    }
}
