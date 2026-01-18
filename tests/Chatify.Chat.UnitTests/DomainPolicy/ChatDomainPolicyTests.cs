using Chatify.Chat.Domain;
using Xunit;

namespace Chatify.Chat.UnitTests.DomainPolicy;

/// <summary>
/// Unit tests for <see cref="ChatDomainPolicy"/> to validate domain policy enforcement.
/// </summary>
/// <remarks>
/// <para>
/// <b>Test Coverage:</b> This test class validates all validation methods in ChatDomainPolicy
/// to ensure they correctly enforce domain invariants and reject invalid inputs.
/// </para>
/// <para>
/// <b>Test Categories:</b>
/// <list type="bullet">
/// <item>Valid inputs that should pass validation</item>
/// <item>Null inputs that should throw ArgumentNullException</item>
/// <item>Empty or whitespace-only inputs that should throw ArgumentException</item>
/// <item>Inputs exceeding maximum length that should throw ArgumentException</item>
/// </list>
/// </para>
/// </remarks>
public class ChatDomainPolicyTests
{
    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateScopeId"/> accepts valid scope identifiers.
    /// </summary>
    [Theory]
    [InlineData("general")]
    [InlineData("random")]
    [InlineData("550e8400-e29b-41d4-a716-446655440000")]
    [InlineData("user-12345")]
    [InlineData("channel_general_discussion")]
    [InlineData("a")] // Minimum valid length (1 character)
    public void ValidateScopeId_WithValidInput_DoesNotThrow(string validScopeId)
    {
        // Act & Assert
        ChatDomainPolicy.ValidateScopeId(validScopeId);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateScopeId"/> accepts a scope identifier at maximum length.
    /// </summary>
    [Fact]
    public void ValidateScopeId_WithMaxLengthInput_DoesNotThrow()
    {
        // Arrange
        var maxLengthScopeId = new string('a', ChatDomainPolicy.MaxScopeIdLength);

        // Act & Assert
        ChatDomainPolicy.ValidateScopeId(maxLengthScopeId);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateScopeId"/> throws ArgumentNullException
    /// when provided with a null scope identifier.
    /// </summary>
    [Fact]
    public void ValidateScopeId_WithNullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ChatDomainPolicy.ValidateScopeId(null!));
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateScopeId"/> throws ArgumentException
    /// when provided with an empty scope identifier.
    /// </summary>
    [Fact]
    public void ValidateScopeId_WithEmptyInput_ThrowsArgumentException()
    {
        // Act
        var exception = Assert.Throws<ArgumentException>(() => ChatDomainPolicy.ValidateScopeId(string.Empty));

        // Assert
        Assert.Contains("cannot be empty or whitespace-only", exception.Message);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateScopeId"/> throws ArgumentException
    /// when provided with a whitespace-only scope identifier.
    /// </summary>
    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData(" \t\n ")]
    public void ValidateScopeId_WithWhitespaceOnlyInput_ThrowsArgumentException(string whitespaceInput)
    {
        // Act
        var exception = Assert.Throws<ArgumentException>(() => ChatDomainPolicy.ValidateScopeId(whitespaceInput));

        // Assert
        Assert.Contains("cannot be empty or whitespace-only", exception.Message);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateScopeId"/> throws ArgumentException
    /// when provided with a scope identifier exceeding the maximum length.
    /// </summary>
    [Fact]
    public void ValidateScopeId_WithInputExceedingMaxLength_ThrowsArgumentException()
    {
        // Arrange
        var tooLongScopeId = new string('x', ChatDomainPolicy.MaxScopeIdLength + 1);

        // Act
        var exception = Assert.Throws<ArgumentException>(() => ChatDomainPolicy.ValidateScopeId(tooLongScopeId));

        // Assert
        Assert.Contains($"cannot exceed {ChatDomainPolicy.MaxScopeIdLength} characters", exception.Message);
        Assert.Contains($"Provided length: {tooLongScopeId.Length}", exception.Message);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateText"/> accepts valid message text.
    /// </summary>
    [Theory]
    [InlineData("Hello, world!")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData("a")]
    [InlineData("🎉🎊🎈")] // Emoji support
    [InlineData("Unicode: 你好世界 Привет мир مرحبا بالعالم")]
    public void ValidateText_WithValidInput_DoesNotThrow(string validText)
    {
        // Act & Assert
        ChatDomainPolicy.ValidateText(validText);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateText"/> accepts text at maximum length.
    /// </summary>
    [Fact]
    public void ValidateText_WithMaxLengthInput_DoesNotThrow()
    {
        // Arrange
        var maxLengthText = new string('a', ChatDomainPolicy.MaxTextLength);

        // Act & Assert
        ChatDomainPolicy.ValidateText(maxLengthText);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateText"/> throws ArgumentNullException
    /// when provided with a null text.
    /// </summary>
    [Fact]
    public void ValidateText_WithNullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ChatDomainPolicy.ValidateText(null!));
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateText"/> throws ArgumentException
    /// when provided with text exceeding the maximum length.
    /// </summary>
    [Fact]
    public void ValidateText_WithInputExceedingMaxLength_ThrowsArgumentException()
    {
        // Arrange
        var tooLongText = new string('x', ChatDomainPolicy.MaxTextLength + 1);

        // Act
        var exception = Assert.Throws<ArgumentException>(() => ChatDomainPolicy.ValidateText(tooLongText));

        // Assert
        Assert.Contains($"cannot exceed {ChatDomainPolicy.MaxTextLength} characters", exception.Message);
        Assert.Contains($"Provided length: {tooLongText.Length}", exception.Message);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateSenderId"/> accepts valid sender identifiers.
    /// </summary>
    [Theory]
    [InlineData("user-12345")]
    [InlineData("550e8400-e29b-41d4-a716-446655440000")]
    [InlineData("admin")]
    [InlineData("a")] // Minimum valid length (1 character)
    public void ValidateSenderId_WithValidInput_DoesNotThrow(string validSenderId)
    {
        // Act & Assert
        ChatDomainPolicy.ValidateSenderId(validSenderId);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateSenderId"/> accepts a sender identifier at maximum length.
    /// </summary>
    [Fact]
    public void ValidateSenderId_WithMaxLengthInput_DoesNotThrow()
    {
        // Arrange
        var maxLengthSenderId = new string('a', ChatDomainPolicy.MaxSenderIdLength);

        // Act & Assert
        ChatDomainPolicy.ValidateSenderId(maxLengthSenderId);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateSenderId"/> throws ArgumentNullException
    /// when provided with a null sender identifier.
    /// </summary>
    [Fact]
    public void ValidateSenderId_WithNullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ChatDomainPolicy.ValidateSenderId(null!));
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateSenderId"/> throws ArgumentException
    /// when provided with an empty sender identifier.
    /// </summary>
    [Fact]
    public void ValidateSenderId_WithEmptyInput_ThrowsArgumentException()
    {
        // Act
        var exception = Assert.Throws<ArgumentException>(() => ChatDomainPolicy.ValidateSenderId(string.Empty));

        // Assert
        Assert.Contains("cannot be empty or whitespace-only", exception.Message);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateSenderId"/> throws ArgumentException
    /// when provided with a whitespace-only sender identifier.
    /// </summary>
    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ValidateSenderId_WithWhitespaceOnlyInput_ThrowsArgumentException(string whitespaceInput)
    {
        // Act
        var exception = Assert.Throws<ArgumentException>(() => ChatDomainPolicy.ValidateSenderId(whitespaceInput));

        // Assert
        Assert.Contains("cannot be empty or whitespace-only", exception.Message);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateSenderId"/> throws ArgumentException
    /// when provided with a sender identifier exceeding the maximum length.
    /// </summary>
    [Fact]
    public void ValidateSenderId_WithInputExceedingMaxLength_ThrowsArgumentException()
    {
        // Arrange
        var tooLongSenderId = new string('x', ChatDomainPolicy.MaxSenderIdLength + 1);

        // Act
        var exception = Assert.Throws<ArgumentException>(() => ChatDomainPolicy.ValidateSenderId(tooLongSenderId));

        // Assert
        Assert.Contains($"cannot exceed {ChatDomainPolicy.MaxSenderIdLength} characters", exception.Message);
        Assert.Contains($"Provided length: {tooLongSenderId.Length}", exception.Message);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateOriginPodId"/> accepts valid pod identifiers.
    /// </summary>
    [Theory]
    [InlineData("chat-api-7d9f4c5b6d-abc12")]
    [InlineData("localhost")]
    [InlineData("development")]
    [InlineData("pod-1")]
    [InlineData("a")] // Minimum valid length (1 character)
    public void ValidateOriginPodId_WithValidInput_DoesNotThrow(string validPodId)
    {
        // Act & Assert
        ChatDomainPolicy.ValidateOriginPodId(validPodId);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateOriginPodId"/> accepts a pod identifier at maximum length.
    /// </summary>
    [Fact]
    public void ValidateOriginPodId_WithMaxLengthInput_DoesNotThrow()
    {
        // Arrange
        var maxLengthPodId = new string('a', ChatDomainPolicy.MaxOriginPodIdLength);

        // Act & Assert
        ChatDomainPolicy.ValidateOriginPodId(maxLengthPodId);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateOriginPodId"/> throws ArgumentNullException
    /// when provided with a null pod identifier.
    /// </summary>
    [Fact]
    public void ValidateOriginPodId_WithNullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ChatDomainPolicy.ValidateOriginPodId(null!));
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateOriginPodId"/> throws ArgumentException
    /// when provided with an empty pod identifier.
    /// </summary>
    [Fact]
    public void ValidateOriginPodId_WithEmptyInput_ThrowsArgumentException()
    {
        // Act
        var exception = Assert.Throws<ArgumentException>(() => ChatDomainPolicy.ValidateOriginPodId(string.Empty));

        // Assert
        Assert.Contains("cannot be empty or whitespace-only", exception.Message);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateOriginPodId"/> throws ArgumentException
    /// when provided with a whitespace-only pod identifier.
    /// </summary>
    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ValidateOriginPodId_WithWhitespaceOnlyInput_ThrowsArgumentException(string whitespaceInput)
    {
        // Act
        var exception = Assert.Throws<ArgumentException>(() => ChatDomainPolicy.ValidateOriginPodId(whitespaceInput));

        // Assert
        Assert.Contains("cannot be empty or whitespace-only", exception.Message);
    }

    /// <summary>
    /// Tests that <see cref="ChatDomainPolicy.ValidateOriginPodId"/> throws ArgumentException
    /// when provided with a pod identifier exceeding the maximum length.
    /// </summary>
    [Fact]
    public void ValidateOriginPodId_WithInputExceedingMaxLength_ThrowsArgumentException()
    {
        // Arrange
        var tooLongPodId = new string('x', ChatDomainPolicy.MaxOriginPodIdLength + 1);

        // Act
        var exception = Assert.Throws<ArgumentException>(() => ChatDomainPolicy.ValidateOriginPodId(tooLongPodId));

        // Assert
        Assert.Contains($"cannot exceed {ChatDomainPolicy.MaxOriginPodIdLength} characters", exception.Message);
        Assert.Contains($"Provided length: {tooLongPodId.Length}", exception.Message);
    }
}
