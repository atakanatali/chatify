package com.chatify.flink.config;

import java.time.Duration;

/**
 * Configuration class for Chatify Flink streaming jobs.
 * <p>
 * This class encapsulates all configuration parameters for the Flink job,
 * including Kafka connection settings, topic names, window sizes, and
 * rate limiting thresholds.
 * </p>
 * <p>
 * <b>Environment Variables:</b> Configuration values are loaded from environment
 * variables with sensible defaults for development. In production, these should
 * be provided via Kubernetes ConfigMaps or Secret volumes.
 * </p>
 * <p>
 * <b>TODO:</b> Consider implementing a more sophisticated configuration mechanism
 * (e.g., Flink's parameter tool, configuration files) for complex deployments.
 * </p>
 *
 * @since 1.0.0
 */
public class FlinkJobConfiguration {

    // Kafka Configuration

    /**
     * The Kafka bootstrap servers (comma-separated list).
     * <p>
     * Environment variable: {@code KAFKA_BOOTSTRAP_SERVERS}
     * Default: {@code chatify-kafka:9092}
     * </p>
     */
    public static final String KAFKA_BOOTSTRAP_SERVERS = getEnv(
            "KAFKA_BOOTSTRAP_SERVERS",
            "chatify-kafka:9092"
    );

    /**
     * The consumer group ID for the Flink job.
     * <p>
     * Environment variable: {@code KAFKA_CONSUMER_GROUP_ID}
     * Default: {@code chatify-flink-processor}
     * </p>
     */
    public static final String KAFKA_CONSUMER_GROUP_ID = getEnv(
            "KAFKA_CONSUMER_GROUP_ID",
            "chatify-flink-processor"
    );

    /**
     * The source topic name for chat events.
     * <p>
     * Environment variable: {@code KAFKA_SOURCE_TOPIC}
     * Default: {@code chat-events}
     * </p>
     */
    public static final String KAFKA_SOURCE_TOPIC = getEnv(
            "KAFKA_SOURCE_TOPIC",
            "chat-events"
    );

    /**
     * The sink topic name for analytics events.
     * <p>
     * Environment variable: {@code KAFKA_ANALYTICS_TOPIC}
     * Default: {@code analytics-events}
     * </p>
     */
    public static final String KAFKA_ANALYTICS_TOPIC = getEnv(
            "KAFKA_ANALYTICS_TOPIC",
            "analytics-events"
    );

    /**
     * The sink topic name for rate limit events.
     * <p>
     * Environment variable: {@code KAFKA_RATE_LIMIT_TOPIC}
     * Default: {@code rate-limit-events}
     * </p>
     */
    public static final String KAFKA_RATE_LIMIT_TOPIC = getEnv(
            "KAFKA_RATE_LIMIT_TOPIC",
            "rate-limit-events"
    );

    // Checkpointing Configuration

    /**
     * The checkpoint interval in milliseconds.
     * <p>
     * Environment variable: {@code CHECKPOINT_INTERVAL_MS}
     * Default: {@code 60000} (60 seconds)
     * </p>
     */
    public static final long CHECKPOINT_INTERVAL_MS = Long.parseLong(getEnv(
            "CHECKPOINT_INTERVAL_MS",
            "60000"
    ));

    /**
     * The minimum pause between checkpoints in milliseconds.
     * <p>
     * Environment variable: {@code CHECKPOINT_MIN_PAUSE_MS}
     * Default: {@code 30000} (30 seconds)
     * </p>
     */
    public static final long CHECKPOINT_MIN_PAUSE_MS = Long.parseLong(getEnv(
            "CHECKPOINT_MIN_PAUSE_MS",
            "30000"
    ));

    /**
     * The checkpoint timeout in milliseconds.
     * <p>
     * Environment variable: {@code CHECKPOINT_TIMEOUT_MS}
     * Default: {@code 600000} (10 minutes)
     * </p>
     */
    public static final long CHECKPOINT_TIMEOUT_MS = Long.parseLong(getEnv(
            "CHECKPOINT_TIMEOUT_MS",
            "600000"
    ));

    /**
     * The maximum number of concurrent checkpoints.
     * <p>
     * Environment variable: {@code CHECKPOINT_MAX_CONCURRENT}
     * Default: {@code 1}
     * </p>
     */
    public static final int CHECKPOINT_MAX_CONCURRENT = Integer.parseInt(getEnv(
            "CHECKPOINT_MAX_CONCURRENT",
            "1"
    ));

    // Analytics Window Configuration

    /**
     * The size of the tumbling window for analytics aggregation in seconds.
     * <p>
     * Environment variable: {@code ANALYTICS_WINDOW_SIZE_SECONDS}
     * Default: {@code 60} (1 minute)
     * </p>
     */
    public static final long ANALYTICS_WINDOW_SIZE_SECONDS = Long.parseLong(getEnv(
            "ANALYTICS_WINDOW_SIZE_SECONDS",
            "60"
    ));

    /**
     * Whether analytics windows should slide (true) or tumble (false).
     * <p>
     * Environment variable: {@code ANALYTICS_SLIDING_WINDOW}
     * Default: {@code false} (tumbling windows)
     * </p>
     * <p>
     * <b>TODO:</b> Implement sliding window support when needed for overlapping analytics.
     * </p>
     */
    public static final boolean ANALYTICS_SLIDING_WINDOW = Boolean.parseBoolean(getEnv(
            "ANALYTICS_SLIDING_WINDOW",
            "false"
    ));

    // Rate Limiting Configuration

    /**
     * The size of the sliding window for rate limit detection in seconds.
     * <p>
     * Environment variable: {@code RATE_LIMIT_WINDOW_SIZE_SECONDS}
     * Default: {@code 60} (1 minute)
     * </p>
     */
    public static final long RATE_LIMIT_WINDOW_SIZE_SECONDS = Long.parseLong(getEnv(
            "RATE_LIMIT_WINDOW_SIZE_SECONDS",
            "60"
    ));

    /**
     * The warning threshold for messages per user per window.
     * <p>
     * Environment variable: {@code RATE_LIMIT_WARNING_THRESHOLD}
     * Default: {@code 80}
     * </p>
     */
    public static final long RATE_LIMIT_WARNING_THRESHOLD = Long.parseLong(getEnv(
            "RATE_LIMIT_WARNING_THRESHOLD",
            "80"
    ));

    /**
     * The hard throttle threshold for messages per user per window.
     * <p>
     * Environment variable: {@code RATE_LIMIT_THROTTLE_THRESHOLD}
     * Default: {@code 100}
     * </p>
     */
    public static final long RATE_LIMIT_THROTTLE_THRESHOLD = Long.parseLong(getEnv(
            "RATE_LIMIT_THROTTLE_THRESHOLD",
            "100"
    ));

    /**
     * The threshold for flagging users for administrative review.
     * <p>
     * Environment variable: {@code RATE_LIMIT_FLAG_THRESHOLD}
     * Default: {@code 200}
     * </p>
     */
    public static final long RATE_LIMIT_FLAG_THRESHOLD = Long.parseLong(getEnv(
            "RATE_LIMIT_FLAG_THRESHOLD",
            "200"
    ));

    // Job Parallelism

    /**
     * The default parallelism for the Flink job.
     * <p>
     * Environment variable: {@code JOB_PARALLELISM}
     * Default: {@code 2}
     * </p>
     */
    public static final int JOB_PARALLELISM = Integer.parseInt(getEnv(
            "JOB_PARALLELISM",
            "2"
    ));

    /**
     * Gets an environment variable value or returns the default if not set.
     *
     * @param key          The environment variable name.
     * @param defaultValue The default value if the variable is not set.
     * @return The environment variable value or the default.
     */
    private static String getEnv(String key, String defaultValue) {
        String value = System.getenv(key);
        return value != null && !value.isEmpty() ? value : defaultValue;
    }

    /**
     * Logs all configuration values at startup for debugging and verification.
     * <p>
     * This method should be called at the beginning of job execution to verify
     * that configuration is loaded correctly.
     * </p>
     */
    public static void logConfiguration() {
        System.out.println("=== Chatify Flink Job Configuration ===");
        System.out.println("Kafka Bootstrap Servers: " + KAFKA_BOOTSTRAP_SERVERS);
        System.out.println("Kafka Consumer Group ID: " + KAFKA_CONSUMER_GROUP_ID);
        System.out.println("Kafka Source Topic: " + KAFKA_SOURCE_TOPIC);
        System.out.println("Kafka Analytics Topic: " + KAFKA_ANALYTICS_TOPIC);
        System.out.println("Kafka Rate Limit Topic: " + KAFKA_RATE_LIMIT_TOPIC);
        System.out.println("Checkpoint Interval (ms): " + CHECKPOINT_INTERVAL_MS);
        System.out.println("Checkpoint Min Pause (ms): " + CHECKPOINT_MIN_PAUSE_MS);
        System.out.println("Checkpoint Timeout (ms): " + CHECKPOINT_TIMEOUT_MS);
        System.out.println("Analytics Window Size (seconds): " + ANALYTICS_WINDOW_SIZE_SECONDS);
        System.out.println("Rate Limit Window Size (seconds): " + RATE_LIMIT_WINDOW_SIZE_SECONDS);
        System.out.println("Rate Limit Warning Threshold: " + RATE_LIMIT_WARNING_THRESHOLD);
        System.out.println("Rate Limit Throttle Threshold: " + RATE_LIMIT_THROTTLE_THRESHOLD);
        System.out.println("Rate Limit Flag Threshold: " + RATE_LIMIT_FLAG_THRESHOLD);
        System.out.println("Job Parallelism: " + JOB_PARALLELISM);
        System.out.println("==========================================");
    }

    /**
     * Validates the configuration and throws an exception if invalid.
     *
     * @throws IllegalArgumentException if configuration values are invalid.
     */
    public static void validateConfiguration() {
        if (CHECKPOINT_INTERVAL_MS <= 0) {
            throw new IllegalArgumentException("Checkpoint interval must be positive");
        }
        if (CHECKPOINT_MIN_PAUSE_MS <= 0) {
            throw new IllegalArgumentException("Checkpoint min pause must be positive");
        }
        if (ANALYTICS_WINDOW_SIZE_SECONDS <= 0) {
            throw new IllegalArgumentException("Analytics window size must be positive");
        }
        if (RATE_LIMIT_WINDOW_SIZE_SECONDS <= 0) {
            throw new IllegalArgumentException("Rate limit window size must be positive");
        }
        if (RATE_LIMIT_WARNING_THRESHOLD <= 0) {
            throw new IllegalArgumentException("Rate limit warning threshold must be positive");
        }
        if (RATE_LIMIT_THROTTLE_THRESHOLD <= RATE_LIMIT_WARNING_THRESHOLD) {
            throw new IllegalArgumentException("Rate limit throttle threshold must be greater than warning threshold");
        }
        if (RATE_LIMIT_FLAG_THRESHOLD <= RATE_LIMIT_THROTTLE_THRESHOLD) {
            throw new IllegalArgumentException("Rate limit flag threshold must be greater than throttle threshold");
        }
        if (JOB_PARALLELISM <= 0) {
            throw new IllegalArgumentException("Job parallelism must be positive");
        }
    }
}
