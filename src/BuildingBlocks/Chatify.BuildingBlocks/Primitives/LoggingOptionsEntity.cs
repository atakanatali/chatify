namespace Chatify.BuildingBlocks.Primitives;

/// <summary>
/// Configuration options for Elasticsearch logging integration in Chatify.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This options class encapsulates all configuration required to connect
/// to and interact with an Elasticsearch cluster for centralized log aggregation and
/// analysis in the Chatify system.
/// </para>
/// <para>
/// <b>Location:</b> This is placed in BuildingBlocks as a shared primitive because:
/// <list type="bullet">
/// <item>Logging configuration is a fundamental cross-cutting concern</item>
/// <item>All services in the modular monolith need the same logging configuration</item>
/// <item>It should be accessible without dependencies on specific modules</item>
/// </list>
/// </para>
/// <para>
/// <b>Integration:</b> These options are used by the Serilog Elasticsearch sink to
/// ship structured logs from Chatify services to an Elasticsearch cluster for:
/// <list type="bullet">
/// <item>Centralized log aggregation across all pods and services</item>
/// <item>Powerful search and filtering of log data</item>
/// <item>Visualization and monitoring via Kibana dashboards</item>
/// <item>Alerting based on log patterns and error rates</item>
/// <item>Long-term log retention and archival</item>
/// </list>
/// </para>
/// <para>
/// <b>Configuration Binding:</b> These options are bound from the IConfiguration
/// instance provided to the DI container. The typical configuration section is
/// "Chatify:Logging". Example appsettings.json:
/// <code><![CDATA[
/// {
///   "Chatify": {
///     "Logging": {
///       "Uri": "http://localhost:9200",
///       "Username": "elastic",
///       "Password": "changeme",
///       "IndexPrefix": "logs-chatify-chatapi"
///     }
///   }
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Index Naming Pattern:</b> The <see cref="IndexPrefix"/> follows the pattern
/// <c>logs-chatify-{servicename}</c> where {servicename} is the service identifier
/// (e.g., "chatapi"). The full index name is constructed as:
/// <c>{IndexPrefix}-{Date}</c>, where Date is in <c>yyyy.MM.dd</c> format.
/// </para>
/// <para>
/// <b>Examples:</b>
/// <list type="bullet">
/// <item><c>logs-chatify-chatapi-2026.01.15</c> - Chatify Chat API</item>
/// <item><c>logs-chatify-gateway-2026.01.15</c> - API Gateway (future)</item>
/// <item><c>logs-chatify-worker-2026.01.15</c> - Background worker (future)</item>
/// </list>
/// </para>
/// <para>
/// <b>Validation:</b> These options are validated when registered via DI.
/// Required fields include Uri and IndexPrefix.
/// </para>
/// <para>
/// <b>Elasticsearch Compatibility:</b> These options work with Elasticsearch 7.x
/// and 8.x, as well as OpenSearch (the AWS-compatible fork).
/// </para>
/// </remarks>
public record LoggingOptionsEntity
{
    /// <summary>
    /// Gets the URI of the Elasticsearch cluster endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required:</b> This field must not be null or whitespace.
    /// </para>
    /// <para>
    /// <b>Format:</b> The URI should include the protocol (http or https), hostname,
    /// and port if not using the default (9200).
    /// </para>
    /// <para>
    /// <b>Examples:</b>
    /// <list type="bullet">
    /// <item><c>http://localhost:9200</c> - Local development</item>
    /// <item><c>https://elastic.example.com:9200</c> - Production with HTTPS</item>
    /// <item><c>https://elastic.prod.example.com</c> - Production with default port</item>
    /// <item><c>http://elasticsearch.logging.svc.cluster.local:9200</c> - Kubernetes service</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Cloud Services:</b> For managed Elasticsearch services:
    /// <list type="bullet">
    /// <item><b>AWS OpenSearch:</b> Use the domain endpoint provided by AWS</item>
    /// <item><b>Elastic Cloud:</b> Use the cloud ID or cluster endpoint</item>
    /// <item><b>Azure Elasticsearch:</b> Use the provisioned cluster endpoint</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>SSL/TLS:</b> Use <c>https://</c> for production deployments to encrypt
    /// log data in transit. Ensure the server certificate is trusted by the client.
    /// </para>
    /// </remarks>
    public string Uri { get; init; } = string.Empty;

    /// <summary>
    /// Gets the username for authenticating with the Elasticsearch cluster.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Optional:</b> This field may be null or whitespace if authentication is not
    /// enabled or is handled differently (e.g., via API keys, cloud IAM, or network policies).
    /// </para>
    /// <para>
    /// <b>Authentication:</b> Elasticsearch supports several authentication methods:
    /// <list type="bullet">
    /// <item><b>Basic Auth:</b> Username/password (this field)</item>
    /// <item><b>API Keys:</b> Encoded API key in headers</item>
    /// <item><b>Cloud IAM:</b> IAM-based authentication (Elastic Cloud, AWS)</item>
    /// <item><b>Anonymous:</b> No authentication (development only)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public string? Username { get; init; }

    /// <summary>
    /// Gets the password for authenticating with the Elasticsearch cluster.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Optional:</b> This field may be null or whitespace if authentication is not
    /// enabled or is handled differently.
    /// </para>
    /// <para>
    /// <b>Security:</b> Passwords should be stored securely using:
    /// <list type="bullet">
    /// <item>Kubernetes secrets</item>
    /// <item>Key Vault / Secrets Manager</item>
    /// <item>Environment variables (with proper access controls)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Never Hardcode:</b> Avoid hardcoding passwords in appsettings.json or
    /// source control. Use configuration providers that inject secrets at runtime.
    /// </para>
    /// </remarks>
    public string? Password { get; init; }

    /// <summary>
    /// Gets the prefix used for Elasticsearch log indices.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required:</b> This field must not be null or whitespace.
    /// </para>
    /// <para>
    /// <b>Default:</b> <c>"logs-chatify"</c> if not specified.
    /// </para>
    /// <para>
    /// <b>Index Naming Pattern:</b> The full index name is constructed as:
    /// <c>{IndexPrefix}-{Date}</c>, where Date is in <c>yyyy.MM.dd</c> format.
    /// </para>
    /// <para>
    /// <b>Examples:</b>
    /// <list type="bullet">
    /// <item><c>logs-chatify-chatapi-2026.01.15</c> - Default for ChatApi</item>
    /// <item><c>chatify-prod-2026.01.15</c> - Environment-specific prefix</item>
    /// <item><c>chatify-worker-2026.01.15</c> - Service-specific prefix</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Naming Conventions:</b>
    /// <list type="bullet">
    /// <item>Use lowercase letters, numbers, and hyphens only</item>
    /// <item>Avoid underscores (can cause issues with some tools)</item>
    /// <item>Include environment or service name if running multiple instances</item>
    /// <item>Must match the pattern <c>logs-*</c> for Elasticsearch index templates</item>
    /// </list>
    /// </para>
    /// </remarks>
    public string IndexPrefix { get; init; } = "logs-chatify";

    /// <summary>
    /// Validates the Elasticsearch options configuration.
    /// </summary>
    /// <returns>
    /// <c>true</c> if all required fields are present and valid; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method performs the following validations:
    /// <list type="bullet">
    /// <item><see cref="Uri"/> is not null or whitespace</item>
    /// <item><see cref="Uri"/> is a well-formed URI</item>
    /// <item><see cref="IndexPrefix"/> is not null or whitespace</item>
    /// </list>
    /// </para>
    /// <para>
    /// This method is called by the DI extension when registering logging
    /// services. If validation fails, an <see cref="ArgumentException"/> is thrown during
    /// service registration to fail fast before the application starts.
    /// </para>
    /// </remarks>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Uri))
        {
            return false;
        }

        if (!System.Uri.TryCreate(Uri, UriKind.Absolute, out _))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(IndexPrefix))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns a string representation of the logging options for logging purposes.
    /// </summary>
    /// <returns>
    /// A string containing the key configuration properties, excluding sensitive data.
    /// </returns>
    /// <remarks>
    /// This method is useful for logging the logging configuration on startup without
    /// exposing sensitive credentials. It includes the URI and index prefix, but only
    /// indicates whether authentication is configured (not the actual credentials).
    /// </remarks>
    public override string ToString()
    {
        bool hasAuth = !string.IsNullOrWhiteSpace(Username);
        return $"LoggingOptionsEntity {{ Uri = {Uri}, IndexPrefix = {IndexPrefix}, Authentication configured = {hasAuth} }}";
    }
}
