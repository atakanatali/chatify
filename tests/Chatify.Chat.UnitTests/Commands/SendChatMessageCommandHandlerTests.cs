using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Application.Commands.SendChatMessage;
using Chatify.Chat.Application.Dtos;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Domain;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Chatify.Chat.UnitTests.Commands;

/// <summary>
/// Unit tests for <see cref="SendChatMessageCommandHandler"/> to validate message send behavior.
/// </summary>
/// <remarks>
/// <para>
/// <b>Test Coverage:</b> This test class validates the command handler's behavior across
/// various scenarios including successful message sends, validation failures, rate limiting,
/// and error handling.
/// </para>
/// <para>
/// <b>Test Categories:</b>
/// <list type="bullet">
/// <item>Successful message send with valid inputs</item>
/// <item>Domain validation failures (null/empty/invalid inputs)</item>
/// <item>Rate limit exceeded scenarios</item>
/// <item>Event production failures</item>
/// <item>Pod identity validation failures</item>
/// </list>
/// </para>
/// </remarks>
public class SendChatMessageCommandHandlerTests
{
    /// <summary>
    /// Tests that a valid message send command succeeds and returns an enriched event.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithValidCommand_SucceedsAndReturnsEnrichedEvent()
    {
        // Arrange
        var mockProducer = new Mock<IChatEventProducerService>(MockBehavior.Strict);
        var mockRateLimit = new Mock<IRateLimitService>(MockBehavior.Strict);
        var mockPodIdentity = new Mock<IPodIdentityService>(MockBehavior.Strict);
        var mockClock = new Mock<IClockService>(MockBehavior.Strict);
        var mockLogger = new Mock<ILogger<SendChatMessageCommandHandler>>(MockBehavior.Loose);

        var testTime = new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var expectedMessageId = Guid.NewGuid();
        var expectedPartition = 0;
        var expectedOffset = 123L;

        mockClock.Setup(c => c.GetUtcNow()).Returns(testTime);
        mockPodIdentity.Setup(p => p.PodId).Returns("chat-api-pod-123");
        mockRateLimit
            .Setup(r => r.CheckAndIncrementAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultEntity.Success());
        mockProducer
            .Setup(p => p.ProduceAsync(
                It.IsAny<ChatEventDto>(),
                It.IsAny<CancellationToken>()))
            .Callback<ChatEventDto, CancellationToken>((evt, ct) =>
            {
                // Set the MessageId to our expected value for verification
                // Note: In real scenario, the handler creates the Guid
            })
            .ReturnsAsync((expectedPartition, expectedOffset));

        var handler = new SendChatMessageCommandHandler(
            mockProducer.Object,
            mockRateLimit.Object,
            mockPodIdentity.Object,
            mockClock.Object,
            mockLogger.Object);

        var command = new SendChatMessageCommand
        {
            SenderId = "user-123",
            Request = new ChatSendRequestDto
            {
                ScopeType = ChatScopeTypeEnum.Channel,
                ScopeId = "general",
                Text = "Hello, world!"
            }
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("user-123", result.Value.ChatEvent.SenderId);
        Assert.Equal(ChatScopeTypeEnum.Channel, result.Value.ChatEvent.ScopeType);
        Assert.Equal("general", result.Value.ChatEvent.ScopeId);
        Assert.Equal("Hello, world!", result.Value.ChatEvent.Text);
        Assert.Equal(testTime.UtcDateTime, result.Value.ChatEvent.CreatedAtUtc);
        Assert.Equal("chat-api-pod-123", result.Value.ChatEvent.OriginPodId);
        Assert.Equal(expectedPartition, result.Value.Partition);
        Assert.Equal(expectedOffset, result.Value.Offset);

        mockProducer.Verify(p => p.ProduceAsync(
            It.IsAny<ChatEventDto>(),
            It.IsAny<CancellationToken>()), Times.Once);

        mockRateLimit.Verify(r => r.CheckAndIncrementAsync(
            It.Is<string>(key => key.Contains("user-123")),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that a command with null sender ID returns a validation failure.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithNullSenderId_ReturnsValidationFailure()
    {
        // Arrange
        var mockProducer = new Mock<IChatEventProducerService>(MockBehavior.Loose);
        var mockRateLimit = new Mock<IRateLimitService>(MockBehavior.Loose);
        var mockPodIdentity = new Mock<IPodIdentityService>(MockBehavior.Loose);
        var mockClock = new Mock<IClockService>(MockBehavior.Loose);
        var mockLogger = new Mock<ILogger<SendChatMessageCommandHandler>>(MockBehavior.Loose);

        var handler = new SendChatMessageCommandHandler(
            mockProducer.Object,
            mockRateLimit.Object,
            mockPodIdentity.Object,
            mockClock.Object,
            mockLogger.Object);

        var command = new SendChatMessageCommand
        {
            SenderId = null!,
            Request = new ChatSendRequestDto
            {
                ScopeType = ChatScopeTypeEnum.Channel,
                ScopeId = "general",
                Text = "Hello"
            }
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
        Assert.Contains("VALIDATION", result.Error.Code);

        // Verify rate limit was never called since validation happens first
        mockRateLimit.Verify(r => r.CheckAndIncrementAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests that a command with empty scope ID returns a validation failure.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithEmptyScopeId_ReturnsValidationFailure()
    {
        // Arrange
        var mockProducer = new Mock<IChatEventProducerService>(MockBehavior.Loose);
        var mockRateLimit = new Mock<IRateLimitService>(MockBehavior.Loose);
        var mockPodIdentity = new Mock<IPodIdentityService>(MockBehavior.Loose);
        var mockClock = new Mock<IClockService>(MockBehavior.Loose);
        var mockLogger = new Mock<ILogger<SendChatMessageCommandHandler>>(MockBehavior.Loose);

        var handler = new SendChatMessageCommandHandler(
            mockProducer.Object,
            mockRateLimit.Object,
            mockPodIdentity.Object,
            mockClock.Object,
            mockLogger.Object);

        var command = new SendChatMessageCommand
        {
            SenderId = "user-123",
            Request = new ChatSendRequestDto
            {
                ScopeType = ChatScopeTypeEnum.Channel,
                ScopeId = string.Empty,
                Text = "Hello"
            }
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
        Assert.Contains("VALIDATION", result.Error.Code);
    }

    /// <summary>
    /// Tests that a command with text exceeding maximum length returns a validation failure.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithTextExceedingMaxLength_ReturnsValidationFailure()
    {
        // Arrange
        var mockProducer = new Mock<IChatEventProducerService>(MockBehavior.Loose);
        var mockRateLimit = new Mock<IRateLimitService>(MockBehavior.Loose);
        var mockPodIdentity = new Mock<IPodIdentityService>(MockBehavior.Loose);
        var mockClock = new Mock<IClockService>(MockBehavior.Loose);
        var mockLogger = new Mock<ILogger<SendChatMessageCommandHandler>>(MockBehavior.Loose);

        var handler = new SendChatMessageCommandHandler(
            mockProducer.Object,
            mockRateLimit.Object,
            mockPodIdentity.Object,
            mockClock.Object,
            mockLogger.Object);

        var tooLongText = new string('x', ChatDomainPolicy.MaxTextLength + 1);

        var command = new SendChatMessageCommand
        {
            SenderId = "user-123",
            Request = new ChatSendRequestDto
            {
                ScopeType = ChatScopeTypeEnum.Channel,
                ScopeId = "general",
                Text = tooLongText
            }
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
        Assert.Contains("VALIDATION", result.Error.Code);
        Assert.Contains("cannot exceed", result.Error.Message);
    }

    /// <summary>
    /// Tests that a command returns a failure when rate limit is exceeded.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenRateLimitExceeded_ReturnsRateLimitFailure()
    {
        // Arrange
        var mockProducer = new Mock<IChatEventProducerService>(MockBehavior.Loose);
        var mockRateLimit = new Mock<IRateLimitService>(MockBehavior.Strict);
        var mockPodIdentity = new Mock<IPodIdentityService>(MockBehavior.Loose);
        var mockClock = new Mock<IClockService>(MockBehavior.Loose);
        var mockLogger = new Mock<ILogger<SendChatMessageCommandHandler>>(MockBehavior.Loose);

        var rateLimitError = ErrorEntity.Validation("Rate limit exceeded", "Too many requests");

        mockRateLimit
            .Setup(r => r.CheckAndIncrementAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultEntity.Failure(rateLimitError));

        var handler = new SendChatMessageCommandHandler(
            mockProducer.Object,
            mockRateLimit.Object,
            mockPodIdentity.Object,
            mockClock.Object,
            mockLogger.Object);

        var command = new SendChatMessageCommand
        {
            SenderId = "user-123",
            Request = new ChatSendRequestDto
            {
                ScopeType = ChatScopeTypeEnum.Channel,
                ScopeId = "general",
                Text = "Hello"
            }
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
        Assert.Contains("Rate", result.Error.Code, StringComparison.OrdinalIgnoreCase);

        // Verify producer was never called since rate limit failed
        mockProducer.Verify(p => p.ProduceAsync(
            It.IsAny<ChatEventDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests that a command returns a failure when pod identity validation fails.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenPodIdentityValidationFails_ReturnsConfigurationError()
    {
        // Arrange
        var mockProducer = new Mock<IChatEventProducerService>(MockBehavior.Loose);
        var mockRateLimit = new Mock<IRateLimitService>(MockBehavior.Strict);
        var mockPodIdentity = new Mock<IPodIdentityService>(MockBehavior.Strict);
        var mockClock = new Mock<IClockService>(MockBehavior.Loose);
        var mockLogger = new Mock<ILogger<SendChatMessageCommandHandler>>(MockBehavior.Loose);

        mockRateLimit
            .Setup(r => r.CheckAndIncrementAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultEntity.Success());

        // Simulate invalid pod identity (empty string)
        mockPodIdentity.Setup(p => p.PodId).Returns(string.Empty);

        var handler = new SendChatMessageCommandHandler(
            mockProducer.Object,
            mockRateLimit.Object,
            mockPodIdentity.Object,
            mockClock.Object,
            mockLogger.Object);

        var command = new SendChatMessageCommand
        {
            SenderId = "user-123",
            Request = new ChatSendRequestDto
            {
                ScopeType = ChatScopeTypeEnum.Channel,
                ScopeId = "general",
                Text = "Hello"
            }
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);

        // Verify producer was never called since pod identity validation failed
        mockProducer.Verify(p => p.ProduceAsync(
            It.IsAny<ChatEventDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests that a command returns a failure when event production throws an exception.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenEventProductionThrowsException_ReturnsEventProductionFailure()
    {
        // Arrange
        var mockProducer = new Mock<IChatEventProducerService>(MockBehavior.Strict);
        var mockRateLimit = new Mock<IRateLimitService>(MockBehavior.Strict);
        var mockPodIdentity = new Mock<IPodIdentityService>(MockBehavior.Strict);
        var mockClock = new Mock<IClockService>(MockBehavior.Strict);
        var mockLogger = new Mock<ILogger<SendChatMessageCommandHandler>>(MockBehavior.Loose);

        var testTime = new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero);

        mockClock.Setup(c => c.GetUtcNow()).Returns(testTime);
        mockPodIdentity.Setup(p => p.PodId).Returns("chat-api-pod-123");
        mockRateLimit
            .Setup(r => r.CheckAndIncrementAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultEntity.Success());

        mockProducer
            .Setup(p => p.ProduceAsync(
                It.IsAny<ChatEventDto>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Kafka broker unavailable"));

        var handler = new SendChatMessageCommandHandler(
            mockProducer.Object,
            mockRateLimit.Object,
            mockPodIdentity.Object,
            mockClock.Object,
            mockLogger.Object);

        var command = new SendChatMessageCommand
        {
            SenderId = "user-123",
            Request = new ChatSendRequestDto
            {
                ScopeType = ChatScopeTypeEnum.Channel,
                ScopeId = "general",
                Text = "Hello"
            }
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
        Assert.Contains("EVENT_PRODUCTION", result.Error.Code);
    }

    /// <summary>
    /// Tests that a command with DirectMessage scope type is processed correctly.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithDirectMessageScope_SucceedsAndReturnsEnrichedEvent()
    {
        // Arrange
        var mockProducer = new Mock<IChatEventProducerService>(MockBehavior.Strict);
        var mockRateLimit = new Mock<IRateLimitService>(MockBehavior.Strict);
        var mockPodIdentity = new Mock<IPodIdentityService>(MockBehavior.Strict);
        var mockClock = new Mock<IClockService>(MockBehavior.Strict);
        var mockLogger = new Mock<ILogger<SendChatMessageCommandHandler>>(MockBehavior.Loose);

        var testTime = new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var expectedPartition = 1;
        var expectedOffset = 456L;

        mockClock.Setup(c => c.GetUtcNow()).Returns(testTime);
        mockPodIdentity.Setup(p => p.PodId).Returns("chat-api-pod-456");
        mockRateLimit
            .Setup(r => r.CheckAndIncrementAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultEntity.Success());
        mockProducer
            .Setup(p => p.ProduceAsync(
                It.IsAny<ChatEventDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((expectedPartition, expectedOffset));

        var handler = new SendChatMessageCommandHandler(
            mockProducer.Object,
            mockRateLimit.Object,
            mockPodIdentity.Object,
            mockClock.Object,
            mockLogger.Object);

        var command = new SendChatMessageCommand
        {
            SenderId = "user-456",
            Request = new ChatSendRequestDto
            {
                ScopeType = ChatScopeTypeEnum.DirectMessage,
                ScopeId = "conv-user123-user456",
                Text = "Private message"
            }
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(ChatScopeTypeEnum.DirectMessage, result.Value.ChatEvent.ScopeType);
        Assert.Equal("conv-user123-user456", result.Value.ChatEvent.ScopeId);
        Assert.Equal("Private message", result.Value.ChatEvent.Text);
        Assert.Equal(expectedPartition, result.Value.Partition);
        Assert.Equal(expectedOffset, result.Value.Offset);
    }

    /// <summary>
    /// Tests that a command with empty text (valid per domain policy) is processed correctly.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithEmptyText_SucceedsAndReturnsEnrichedEvent()
    {
        // Arrange
        var mockProducer = new Mock<IChatEventProducerService>(MockBehavior.Strict);
        var mockRateLimit = new Mock<IRateLimitService>(MockBehavior.Strict);
        var mockPodIdentity = new Mock<IPodIdentityService>(MockBehavior.Strict);
        var mockClock = new Mock<IClockService>(MockBehavior.Strict);
        var mockLogger = new Mock<ILogger<SendChatMessageCommandHandler>>(MockBehavior.Loose);

        mockClock.Setup(c => c.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
        mockPodIdentity.Setup(p => p.PodId).Returns("chat-api-pod");
        mockRateLimit
            .Setup(r => r.CheckAndIncrementAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultEntity.Success());
        mockProducer
            .Setup(p => p.ProduceAsync(
                It.IsAny<ChatEventDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, 789L));

        var handler = new SendChatMessageCommandHandler(
            mockProducer.Object,
            mockRateLimit.Object,
            mockPodIdentity.Object,
            mockClock.Object,
            mockLogger.Object);

        var command = new SendChatMessageCommand
        {
            SenderId = "user-789",
            Request = new ChatSendRequestDto
            {
                ScopeType = ChatScopeTypeEnum.Channel,
                ScopeId = "attachments-only",
                Text = string.Empty // Empty text is valid per domain policy
            }
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(string.Empty, result.Value.ChatEvent.Text);
    }

    /// <summary>
    /// Tests that event production failure with TimeoutException is handled correctly.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenEventProductionTimesOut_ReturnsEventProductionFailure()
    {
        // Arrange
        var mockProducer = new Mock<IChatEventProducerService>(MockBehavior.Strict);
        var mockRateLimit = new Mock<IRateLimitService>(MockBehavior.Strict);
        var mockPodIdentity = new Mock<IPodIdentityService>(MockBehavior.Strict);
        var mockClock = new Mock<IClockService>(MockBehavior.Strict);
        var mockLogger = new Mock<ILogger<SendChatMessageCommandHandler>>(MockBehavior.Loose);

        mockClock.Setup(c => c.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
        mockPodIdentity.Setup(p => p.PodId).Returns("chat-api-pod");
        mockRateLimit
            .Setup(r => r.CheckAndIncrementAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultEntity.Success());

        mockProducer
            .Setup(p => p.ProduceAsync(
                It.IsAny<ChatEventDto>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Kafka connection timeout"));

        var handler = new SendChatMessageCommandHandler(
            mockProducer.Object,
            mockRateLimit.Object,
            mockPodIdentity.Object,
            mockClock.Object,
            mockLogger.Object);

        var command = new SendChatMessageCommand
        {
            SenderId = "user-timeout",
            Request = new ChatSendRequestDto
            {
                ScopeType = ChatScopeTypeEnum.Channel,
                ScopeId = "general",
                Text = "Hello"
            }
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
        Assert.Contains("EVENT_PRODUCTION", result.Error.Code);
    }

    /// <summary>
    /// Tests that the rate limit key includes the sender ID.
    /// </summary>
    [Fact]
    public async Task HandleAsync_VerifiesRateLimitKeyIncludesSenderId()
    {
        // Arrange
        var mockProducer = new Mock<IChatEventProducerService>(MockBehavior.Loose);
        var mockRateLimit = new Mock<IRateLimitService>(MockBehavior.Strict);
        var mockPodIdentity = new Mock<IPodIdentityService>(MockBehavior.Loose);
        var mockClock = new Mock<IClockService>(MockBehavior.Loose);
        var mockLogger = new Mock<ILogger<SendChatMessageCommandHandler>>(MockBehavior.Loose);

        var senderId = "user-specific-123";
        string? capturedKey = null;

        mockRateLimit
            .Setup(r => r.CheckAndIncrementAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, int, int, CancellationToken>((key, _, _, _) => capturedKey = key)
            .ReturnsAsync(ResultEntity.Success());

        var handler = new SendChatMessageCommandHandler(
            mockProducer.Object,
            mockRateLimit.Object,
            mockPodIdentity.Object,
            mockClock.Object,
            mockLogger.Object);

        var command = new SendChatMessageCommand
        {
            SenderId = senderId,
            Request = new ChatSendRequestDto
            {
                ScopeType = ChatScopeTypeEnum.Channel,
                ScopeId = "general",
                Text = "Hello"
            }
        };

        // Act
        await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedKey);
        Assert.Contains(senderId, capturedKey);
    }
}
