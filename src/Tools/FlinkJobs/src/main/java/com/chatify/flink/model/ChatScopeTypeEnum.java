package com.chatify.flink.model;

/**
 * Enumeration representing the type of chat scope for a chat event.
 * <p>
 * This enum mirrors the ChatScopeTypeEnum from the Chatify.Chat.Domain layer
 * and defines whether a chat event belongs to a Channel or DirectMessage scope.
 * </p>
 * <p>
 * <b>Serialization:</b> This enum is serialized to/from JSON when deserializing
 * chat events from Kafka. The value must match exactly with the C# enum values.
 * </p>
 *
 * @see ChatEventEntity
 * @since 1.0.0
 */
public enum ChatScopeTypeEnum {
    /**
     * Represents a channel-based chat scope where messages are broadcast
     * to all members of the channel.
     */
    CHANNEL("Channel"),

    /**
     * Represents a direct message chat scope for one-to-one or small group
     * conversations between specific users.
     */
    DIRECT_MESSAGE("DirectMessage");

    private final String value;

    /**
     * Constructs a ChatScopeTypeEnum with the specified string value.
     *
     * @param value The string representation of the scope type as serialized in JSON.
     */
    ChatScopeTypeEnum(String value) {
        this.value = value;
    }

    /**
     * Gets the string value of this enum as it appears in JSON serialization.
     *
     * @return The string value of this scope type.
     */
    public String getValue() {
        return value;
    }

    /**
     * Parses a string value to the corresponding ChatScopeTypeEnum.
     * <p>
     * This method is used when deserializing chat events from Kafka where
     * the scope type is represented as a string in JSON.
     * </p>
     *
     * @param value The string value to parse (e.g., "Channel", "DirectMessage").
     * @return The corresponding ChatScopeTypeEnum.
     * @throws IllegalArgumentException if the value does not match any known scope type.
     */
    public static ChatScopeTypeEnum fromValue(String value) {
        for (ChatScopeTypeEnum type : ChatScopeTypeEnum.values()) {
            if (type.value.equals(value)) {
                return type;
            }
        }
        throw new IllegalArgumentException("Unknown ChatScopeTypeEnum value: " + value);
    }

    @Override
    public String toString() {
        return value;
    }
}
