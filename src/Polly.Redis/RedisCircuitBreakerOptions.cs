namespace CircuitBreaker.Redis.Distributed;

/// <summary>
/// Configuration options for Redis-backed circuit breaker.
/// </summary>
public class RedisCircuitBreakerOptions
{
    /// <summary>
    /// Unique identifier for this circuit breaker. All instances using the same ID share state.
    /// </summary>
    public string CircuitBreakerId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Redis connection string. Works with Azure, AWS, Redis Cloud, or self-hosted.
    /// Examples:
    /// - "localhost:6379"
    /// - "your-cache.redis.cache.windows.net:6380,password=xxx,ssl=True"
    /// - "your-cluster.cache.amazonaws.com:6379"
    /// </summary>
    public string RedisConfiguration { get; set; } = "localhost:6379";

    /// <summary>
    /// Key prefix for Redis keys.
    /// </summary>
    public string KeyPrefix { get; set; } = "cb:distributed";

    /// <summary>
    /// Failure threshold ratio (0.0 to 1.0). Circuit breaks when failures exceed this ratio.
    /// Default: 0.5 (50% failure rate)
    /// </summary>
    public double FailureRatio { get; set; } = 0.5;

    /// <summary>
    /// Time window for sampling failures (sliding window).
    /// Default: 10 seconds
    /// </summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Minimum number of calls before circuit can break.
    /// Default: 5
    /// </summary>
    public int MinimumThroughput { get; set; } = 5;

    /// <summary>
    /// How long the circuit stays open before attempting half-open.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Fallback to local in-memory state if Redis is unavailable.
    /// Default: true
    /// </summary>
    public bool EnableFallbackToInMemory { get; set; } = true;

    /// <summary>
    /// Timeout for Redis operations.
    /// Default: 5 seconds
    /// </summary>
    public TimeSpan RedisOperationTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Called when circuit opens.
    /// </summary>
    public Action<InternalCircuitStateChange>? OnCircuitOpened { get; set; }

    /// <summary>
    /// Called when circuit closes.
    /// </summary>
    public Action<InternalCircuitStateChange>? OnCircuitClosed { get; set; }

    /// <summary>
    /// Called when circuit transitions to half-open.
    /// </summary>
    public Action<InternalCircuitStateChange>? OnCircuitHalfOpen { get; set; }
}

/// <summary>
/// Internal state change record (different from public CircuitStateChange).
/// </summary>
public record InternalCircuitStateChange(
    string CircuitBreakerId,
    CircuitState PriorState,
    CircuitState NewState,
    DateTimeOffset Timestamp,
    Exception? TriggeringException = null);
