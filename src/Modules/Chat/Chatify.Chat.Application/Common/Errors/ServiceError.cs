using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Application.Common.Constants;

namespace Chatify.Chat.Application.Common.Errors;

/// <summary>
/// Provides factory methods for creating <see cref="ErrorEntity"/> instances
/// with standardized error codes and messages for the Chatify Chat Application.
/// This eliminates magic strings and provides type-safe error creation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> Instead of creating ErrorEntity instances with magic strings
/// throughout the codebase, use these factory methods. This centralizes error
/// definitions and prevents typos in error codes.
/// </para>
/// <para>
/// <b>Usage Pattern:</b>
/// <code><![CDATA[
/// return ResultEntity.Failure(ServiceError.Chat.ValidationFailed("Invalid scope ID"));
/// return ResultEntity.Failure(ServiceError.Chat.RateLimitExceeded(userId));
/// return ResultEntity.Failure(ServiceError.System.ConfigurationError("Pod ID is missing"));
/// ]]></code>
/// </para>
/// </remarks>
public static class ServiceError
{
    /// <summary>
    /// Provides error factory methods for chat-specific errors in the Chatify system.
    /// </summary>
    public static class Chat
    {
        /// <summary>
        /// Creates a validation error for chat message input validation failures.
        /// </summary>
        /// <param name="message">The human-readable error message describing what failed validation.</param>
        /// <param name="exception">Optional exception that caused the validation failure.</param>
        /// <returns>An ErrorEntity with validation error code and message.</returns>
        /// <remarks>
        /// Use this when domain policy validation fails (e.g., invalid scope ID,
        /// message text too long, invalid sender ID).
        /// </remarks>
        public static ErrorEntity ValidationFailed(string message, Exception? exception = null)
        {
            return new ErrorEntity(
                ChatifyConstants.ErrorCodes.ValidationError,
                message,
                exception);
        }

        /// <summary>
        /// Creates a rate limit exceeded error for chat message sending.
        /// </summary>
        /// <param name="userId">The user ID that exceeded the rate limit.</param>
        /// <param name="exception">Optional exception from the rate limit service.</param>
        /// <returns>An ErrorEntity with rate limit exceeded code and message.</returns>
        /// <remarks>
        /// Use this when a user has exceeded the message sending threshold
        /// within the configured time window.
        /// </remarks>
        public static ErrorEntity RateLimitExceeded(string userId, Exception? exception = null)
        {
            return new ErrorEntity(
                ChatifyConstants.ErrorCodes.RateLimitExceeded,
                $"User {userId} has exceeded the message sending rate limit. Please try again later.",
                exception);
        }
    }

    /// <summary>
    /// Provides error factory methods for system-level errors in the Chatify system.
    /// </summary>
    public static class System
    {
        /// <summary>
        /// Creates a configuration error for system misconfiguration issues.
        /// </summary>
        /// <param name="message">The human-readable error message describing the configuration issue.</param>
        /// <param name="exception">Optional exception that revealed the configuration error.</param>
        /// <returns>An ErrorEntity with configuration error code and message.</returns>
        /// <remarks>
        /// Use this when the system is not properly configured (e.g., missing
        /// environment variables, invalid pod ID, missing service registrations).
        /// </remarks>
        public static ErrorEntity ConfigurationError(string message, Exception? exception = null)
        {
            return new ErrorEntity(
                ChatifyConstants.ErrorCodes.ConfigurationError,
                message,
                exception);
        }

        /// <summary>
        /// Creates a configuration error with a generic user-friendly message.
        /// </summary>
        /// <param name="exception">Optional exception that revealed the configuration error.</param>
        /// <returns>An ErrorEntity with configuration error code and generic message.</returns>
        /// <remarks>
        /// Use this overload when you want to hide technical details from the user
        /// while still logging the actual exception for debugging.
        /// </remarks>
        public static ErrorEntity ConfigurationError(Exception? exception = null)
        {
            return new ErrorEntity(
                ChatifyConstants.ErrorCodes.ConfigurationError,
                "The system is not properly configured. Please contact support.",
                exception);
        }
    }

    /// <summary>
    /// Provides error factory methods for messaging/infrastructure errors in the Chatify system.
    /// </summary>
    public static class Messaging
    {
        /// <summary>
        /// Creates an event production error for messaging system failures.
        /// </summary>
        /// <param name="message">The human-readable error message describing the production failure.</param>
        /// <param name="exception">Optional exception from the messaging system.</param>
        /// <returns>An ErrorEntity with event production failed code and message.</returns>
        /// <remarks>
        /// Use this when the messaging system (Kafka, Redis, etc.) fails to
        /// accept or produce an event. This could be due to network issues,
        /// broker unavailability, or timeout.
        /// </remarks>
        public static ErrorEntity EventProductionFailed(string message, Exception? exception = null)
        {
            return new ErrorEntity(
                ChatifyConstants.ErrorCodes.EventProductionFailed,
                message,
                exception);
        }

        /// <summary>
        /// Creates an event production error with a generic user-friendly message.
        /// </summary>
        /// <param name="exception">Optional exception from the messaging system.</param>
        /// <returns>An ErrorEntity with event production failed code and generic message.</returns>
        /// <remarks>
        /// Use this overload when you want to provide a generic message to users
        /// while logging the actual exception for debugging.
        /// </remarks>
        public static ErrorEntity EventProductionFailed(Exception? exception = null)
        {
            return new ErrorEntity(
                ChatifyConstants.ErrorCodes.EventProductionFailed,
                "Failed to send message due to a temporary system issue. Please try again.",
                exception);
        }
    }
}
