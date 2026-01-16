using System.Net;
using System.Text;
using System.Text.Json;
using Chatify.Api;
using Chatify.Chat.Application.Dtos;
using Chatify.Chat.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Chatify.Api.IntegrationTests;

/// <summary>
/// Custom WebApplicationFactory for Chatify API integration tests.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This factory configures the test host to use in-memory implementations
/// of external services (message broker, databases, etc.) for testing without requiring
/// external infrastructure.
/// </para>
/// </remarks>
public class ChatifyWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Configures the test host with in-memory services.
    /// </summary>
    /// <param name="builder">The web host builder to configure.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Use in-memory message broker for tests
        builder.UseSetting("Chatify:MessageBroker:UseInMemoryBroker", "true");
        // Enable test mode to skip external dependencies (database, caching, background services)
        builder.UseSetting("Chatify:TestMode", "true");
    }
}

/// <summary>
/// Integration tests for the Chatify API using WebApplicationFactory.
/// </summary>
/// <remarks>
/// <para>
/// <b>Test Scope:</b> This test class validates the end-to-end behavior of the
/// Chatify API, including HTTP request handling, command execution, and event
/// production using the in-memory message broker.
/// </para>
/// <para>
/// <b>In-Memory Broker:</b> Tests use the in-memory message broker by setting
/// the <c>Chatify:MessageBroker:UseInMemoryBroker</c> configuration flag to <c>true</c>.
/// This eliminates the need for an external Kafka broker during testing.
/// </para>
/// <para>
/// <b>Test Categories:</b>
/// <list type="bullet">
/// <item>Health check endpoint</item>
/// <item>Successful message send</item>
/// <item>Validation failures</item>
/// <item>Rate limiting behavior</item>
/// </list>
/// </para>
/// </remarks>
public class ChatApiIntegrationTests : IClassFixture<ChatifyWebApplicationFactory>
{
    /// <summary>
    /// Gets the WebApplicationFactory instance for creating test HTTP clients.
    /// </summary>
    private readonly ChatifyWebApplicationFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatApiIntegrationTests"/> class.
    /// </summary>
    /// <param name="factory">
    /// The WebApplicationFactory for creating test HTTP clients.
    /// </param>
    public ChatApiIntegrationTests(ChatifyWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Tests that the health check endpoint returns a healthy status.
    /// </summary>
    [Fact]
    public async Task HealthEndpoint_ReturnsHealthyStatus()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/chat/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", content);
    }

    /// <summary>
    /// Tests that a valid message send request succeeds and returns an enriched event.
    /// </summary>
    [Fact]
    public async Task SendMessage_WithValidRequest_SucceedsAndReturnsEnrichedEvent()
    {
        // Arrange
        var client = _factory.CreateClient();
        var senderId = "integration-test-user";
        var request = new ChatSendRequestDto
        {
            ScopeType = ChatScopeTypeEnum.Channel,
            ScopeId = "integration-test-channel",
            Text = "Hello from integration tests!"
        };

        // Act
        var httpResponse = await client.PostAsync($"/api/chat/send/{senderId}", CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);

        var content = await httpResponse.Content.ReadAsStringAsync();
        Assert.Contains(senderId, content);
        Assert.Contains("integration-test-channel", content);
        Assert.Contains("Hello from integration tests!", content);
    }

    /// <summary>
    /// Tests that a message send request with an empty scope ID returns a validation error.
    /// </summary>
    [Fact]
    public async Task SendMessage_WithEmptyScopeId_ReturnsValidationError()
    {
        // Arrange
        var client = _factory.CreateClient();
        var senderId = "integration-test-user";
        var request = new ChatSendRequestDto
        {
            ScopeType = ChatScopeTypeEnum.Channel,
            ScopeId = string.Empty,
            Text = "Hello"
        };

        // Act
        var httpResponse = await client.PostAsync($"/api/chat/send/{senderId}", CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, httpResponse.StatusCode);
    }

    /// <summary>
    /// Tests that a message send request with text exceeding maximum length returns a validation error.
    /// </summary>
    [Fact]
    public async Task SendMessage_WithTextExceedingMaxLength_ReturnsValidationError()
    {
        // Arrange
        var client = _factory.CreateClient();
        var senderId = "integration-test-user";
        var tooLongText = new string('x', ChatDomainPolicy.MaxTextLength + 1);
        var request = new ChatSendRequestDto
        {
            ScopeType = ChatScopeTypeEnum.Channel,
            ScopeId = "test",
            Text = tooLongText
        };

        // Act
        var httpResponse = await client.PostAsync($"/api/chat/send/{senderId}", CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, httpResponse.StatusCode);

        var content = await httpResponse.Content.ReadAsStringAsync();
        Assert.Contains("cannot exceed", content);
    }

    /// <summary>
    /// Tests that multiple messages with the same scope ID are assigned to the same partition.
    /// </summary>
    [Fact]
    public async Task SendMessage_WithSameScopeId_GoesToSamePartition()
    {
        // Arrange
        var client = _factory.CreateClient();
        var senderId = "partition-test-user";
        var scopeId = "same-scope-test";
        var request1 = new ChatSendRequestDto
        {
            ScopeType = ChatScopeTypeEnum.Channel,
            ScopeId = scopeId,
            Text = "First message"
        };
        var request2 = new ChatSendRequestDto
        {
            ScopeType = ChatScopeTypeEnum.Channel,
            ScopeId = scopeId,
            Text = "Second message"
        };

        // Act
        var response1 = await client.PostAsync($"/api/chat/send/{senderId}", CreateJsonContent(request1));
        var response2 = await client.PostAsync($"/api/chat/send/{senderId}", CreateJsonContent(request2));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        var content1 = await response1.Content.ReadAsStringAsync();
        var content2 = await response2.Content.ReadAsStringAsync();

        // Both messages should be in the same partition
        var partition1 = ExtractPartition(content1);
        var partition2 = ExtractPartition(content2);
        Assert.Equal(partition1, partition2);

        // Offsets should be different
        var offset1 = ExtractOffset(content1);
        var offset2 = ExtractOffset(content2);
        Assert.NotEqual(offset1, offset2);
    }

    /// <summary>
    /// Tests that a DirectMessage scope type is processed correctly.
    /// </summary>
    [Fact]
    public async Task SendMessage_WithDirectMessageScope_Succeeds()
    {
        // Arrange
        var client = _factory.CreateClient();
        var senderId = "dm-test-user";
        var request = new ChatSendRequestDto
        {
            ScopeType = ChatScopeTypeEnum.DirectMessage,
            ScopeId = "conv-user1-user2",
            Text = "Private message"
        };

        // Act
        var httpResponse = await client.PostAsync($"/api/chat/send/{senderId}", CreateJsonContent(request));

        // Assert
        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);

        var content = await httpResponse.Content.ReadAsStringAsync();
        Assert.Contains("conv-user1-user2", content);
        Assert.Contains("Private message", content);
    }

    /// <summary>
    /// Creates JSON content from an object.
    /// </summary>
    private static StringContent CreateJsonContent<T>(T obj)
    {
        return new StringContent(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");
    }

    /// <summary>
    /// Extracts the partition value from JSON response content.
    /// </summary>
    private static int ExtractPartition(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("partition").GetInt32();
    }

    /// <summary>
    /// Extracts the offset value from JSON response content.
    /// </summary>
    private static long ExtractOffset(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("offset").GetInt64();
    }
}
