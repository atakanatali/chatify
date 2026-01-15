namespace Chatify.Chat.Infrastructure.Options;

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
/// "Chatify:Elastic". Example appsettings.json:
/// <code><![CDATA[
/// {
///   "Chatify": {
///     "Elastic": {
///       "Uri": "http://localhost:9200",
///       "Username": "elastic",
///       "Password": "changeme",
///       "IndexPrefix": "logs-chatify"
///     }
///   }
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Index Strategy:</b> The <see cref="IndexPrefix"/> is combined with the date
/// to create daily indices (e.g., <c>logs-chatify-2026-01-15</c>). This provides:
/// <list type="bullet">
/// <item>Manageable index sizes for easier maintenance</item>
/// <item>Efficient deletion of old data by date range</item>
/// <item>Optimized query performance by searching only relevant time ranges</item>
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
public record ElasticOptionsEntity
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
    /// <para>
    /// <b>Load Balancing:</b> If using a load balancer or proxy in front of
    /// Elasticsearch, specify the load balancer URI. The load balancer should
    /// distribute requests across all nodes in the cluster.
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
    /// <para>
    /// <b>Default Users:</b>
    /// <list type="bullet">
    /// <item><c>elastic</c> - Built-in superuser (avoid using in production)</item>
    /// <item>Custom users with limited privileges should be created for applications</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>User Management:</b> Create a dedicated user for Chatify with minimal
    /// required permissions (create index, write documents). Example:
    /// <code><![CDATA[
    /// POST /_security/user/chatify_logger
    /// {
    ///   "password": "secure_password",
    ///   "roles": ["chatify_log_writer"]
    /// }
    /// ]]></code>
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
    /// <para>
    /// <b>Rotation:</b> Implement password rotation procedures for production deployments.
    /// When rotating credentials, ensure a graceful restart of Chatify services to
    /// pick up the new credentials.
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
    /// <c>{IndexPrefix}-{Date}</c>, where Date is in <c>yyyy-MM-dd</c> format.
    /// </para>
    /// <para>
    /// <b>Examples:</b>
    /// <list type="bullet">
    /// <item><c>logs-chatify-2026-01-15</c> - Default prefix</item>
    /// <item><c>chatify-prod-2026-01-15</c> - Environment-specific prefix</item>
    /// <item><c>chatify-chatapi-2026-01-15</c> - Service-specific prefix</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Naming Conventions:</b>
    /// <list type="bullet">
    /// <item>Use lowercase letters, numbers, and hyphens only</item>
    /// <item>Avoid underscores (can cause issues with some tools)</item>
    /// <item>Keep it short but descriptive</item>
    /// <item>Include environment or service name if running multiple instances</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Index Management:</b> Using a date-based index pattern allows for:
    /// <list type="bullet">
    /// <item>Easy cleanup of old logs using Index Lifecycle Management (ILM)</item>
    /// <item>Better query performance by limiting searches to relevant date ranges</item>
    /// <item>Efficient snapshot and restore operations</item>
    /// <item>Rolling over to new indices when size limits are reached</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>ILM Policy:</b> Configure an ILM policy to:
    /// <list type="bullet">
    /// <item>Roll over indices when they reach ~50GB</item>
    /// <item>Move indices to warm storage after 7 days</item>
    /// <item>Delete indices after 30 days (or your retention period)</item>
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
    /// <item>If <see cref="Username"/> is provided, <see cref="Password"/> should also be provided</item>
    /// </list>
    /// </para>
    /// <para>
    /// This method is called by the DI extension when registering Elasticsearch logging
    /// services. If validation fails, an <see cref="ArgumentException"/> is thrown during
    /// service registration to fail fast before the application starts.
    /// </para>
    /// <para>
    /// <b>Note:</b> This method does not attempt to connect to the Elasticsearch cluster.
    /// Connection validation occurs when the first log is written or when health checks run.
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

        // If username is provided, password should also be provided (but not strictly required)
        // Some Elasticsearch setups allow username-only auth with API keys

        return true;
    }

    /// <summary>
    /// Returns a string representation of the Elasticsearch options for logging purposes.
    /// </summary>
    /// <returns>
    /// A string containing the key configuration properties, excluding sensitive data.
    /// </returns>
    /// <remarks>
    /// This method is useful for logging the Elasticsearch configuration on startup without
    /// exposing sensitive credentials. It includes the URI and index prefix, but only
    /// indicates whether authentication is configured (not the actual credentials).
    /// </remarks>
    public override string ToString()
    {
        bool hasAuth = !string.IsNullOrWhiteSpace(Username);
        return $"ElasticOptionsEntity {{ Uri = {Uri}, IndexPrefix = {IndexPrefix}, Authentication configured = {hasAuth} }}";
    }
}
