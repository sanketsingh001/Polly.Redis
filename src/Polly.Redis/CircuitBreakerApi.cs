// Polly.Redis - Developer-Friendly Distributed Circuit Breaker
// Simple to use, highly configurable, works with any scenario

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Polly.Redis;

/// <summary>
/// SIMPLE USAGE:
/// 
/// var cb = CircuitBreaker.Create("my-circuit", "localhost:6379");
/// var result = await cb.Execute(async () => await CallApi());
/// 
/// WITH FALLBACK:
/// 
/// var result = await cb.Execute(
///     action: async () => await PrimaryApi(),
///     fallback: async () => await BackupApi()
/// );
/// 
/// ADVANCED CONFIG:
/// 
/// var cb = CircuitBreaker.Create(config => config
///     .WithRedis("your-redis:6379")
///     .WithCircuitId("payment-api")
///     .FailWhen(failureRatio: 0.5, minimumCalls: 5)
///     .StayOpenFor(seconds: 30)
///     .OnStateChange(change => logger.Log(change))
/// );
/// </summary>
public static class CircuitBreaker
{
    /// <summary>
    /// Simplest way to create a circuit breaker.
    /// </summary>
    /// <param name="circuitId">Unique ID - instances with same ID share state</param>
    /// <param name="redis">Redis connection string (any provider works)</param>
    public static ICircuitBreaker Create(string circuitId, string redis)
    {
        return new Builder()
            .WithCircuitId(circuitId)
            .WithRedis(redis)
            .Build();
    }

    /// <summary>
    /// Create with configuration builder for full control.
    /// </summary>
    public static ICircuitBreaker Create(Action<Builder> configure)
    {
        var builder = new Builder();
        configure(builder);
        return builder.Build();
    }

    /// <summary>
    /// Fluent configuration builder.
    /// </summary>
    public class Builder
    {
        private string _circuitId = "default";
        private string _redis = "localhost:6379";
        private double _failureRatio = 0.5;
        private int _minimumCalls = 5;
        private TimeSpan _breakDuration = TimeSpan.FromSeconds(30);
        private TimeSpan _samplingWindow = TimeSpan.FromSeconds(10);
        private bool _fallbackToMemory = true;
        private Action<StateChange>? _onStateChange;
        private ILogger? _logger;

        /// <summary>
        /// Set the circuit breaker ID. Instances with same ID share state.
        /// </summary>
        public Builder WithCircuitId(string id)
        {
            _circuitId = id;
            return this;
        }

        /// <summary>
        /// Set Redis connection. Works with Azure, AWS, Redis Cloud, or self-hosted.
        /// Examples:
        /// - "localhost:6379"
        /// - "your-cache.redis.cache.windows.net:6380,ssl=True,password=xxx"
        /// - "your-cluster.cache.amazonaws.com:6379"
        /// </summary>
        public Builder WithRedis(string connectionString)
        {
            _redis = connectionString;
            return this;
        }

        /// <summary>
        /// Configure when circuit should break.
        /// </summary>
        /// <param name="failureRatio">Break when failures exceed this ratio (0.0-1.0). Default: 0.5 (50%)</param>
        /// <param name="minimumCalls">Minimum calls before circuit can break. Default: 5</param>
        public Builder FailWhen(double failureRatio = 0.5, int minimumCalls = 5)
        {
            _failureRatio = failureRatio;
            _minimumCalls = minimumCalls;
            return this;
        }

        /// <summary>
        /// How long circuit stays open before trying again.
        /// </summary>
        public Builder StayOpenFor(TimeSpan duration)
        {
            _breakDuration = duration;
            return this;
        }

        /// <summary>
        /// How long circuit stays open (in seconds).
        /// </summary>
        public Builder StayOpenFor(int seconds)
        {
            _breakDuration = TimeSpan.FromSeconds(seconds);
            return this;
        }

        /// <summary>
        /// Time window for measuring failure ratio. Default: 10 seconds.
        /// </summary>
        public Builder MeasureFailuresOver(TimeSpan window)
        {
            _samplingWindow = window;
            return this;
        }

        /// <summary>
        /// Called when circuit state changes (opened, closed, half-open).
        /// </summary>
        public Builder OnStateChange(Action<StateChange> handler)
        {
            _onStateChange = handler;
            return this;
        }

        /// <summary>
        /// If Redis unavailable, fallback to local in-memory (per instance). Default: true.
        /// </summary>
        public Builder FallbackToMemory(bool enable = true)
        {
            _fallbackToMemory = enable;
            return this;
        }

        /// <summary>
        /// Provide a logger for debugging.
        /// </summary>
        public Builder WithLogger(ILogger logger)
        {
            _logger = logger;
            return this;
        }

        /// <summary>
        /// Build the circuit breaker.
        /// </summary>
        public ICircuitBreaker Build()
        {
            var options = new RedisCircuitBreakerOptions
            {
                CircuitBreakerId = _circuitId,
                RedisConfiguration = _redis,
                FailureRatio = _failureRatio,
                MinimumThroughput = _minimumCalls,
                BreakDuration = _breakDuration,
                SamplingDuration = _samplingWindow,
                EnableFallbackToInMemory = _fallbackToMemory,
                OnCircuitOpened = _onStateChange != null ? c => _onStateChange(new StateChange(c.CircuitBreakerId, "Opened", c.Timestamp)) : null,
                OnCircuitClosed = _onStateChange != null ? c => _onStateChange(new StateChange(c.CircuitBreakerId, "Closed", c.Timestamp)) : null,
                OnCircuitHalfOpen = _onStateChange != null ? c => _onStateChange(new StateChange(c.CircuitBreakerId, "HalfOpen", c.Timestamp)) : null
            };

            var logger = _logger ?? LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning))
                .CreateLogger<RedisCircuitBreaker>();

            var innerCb = new RedisCircuitBreaker(options, logger);
            return new CircuitBreakerWrapper(innerCb);
        }
    }
}

/// <summary>
/// Simple interface for circuit breaker - easy to understand.
/// </summary>
public interface ICircuitBreaker : IAsyncDisposable
{
    /// <summary>
    /// Current circuit state: Open, Closed, HalfOpen, or Isolated.
    /// </summary>
    string State { get; }

    /// <summary>
    /// Is the circuit currently allowing calls?
    /// </summary>
    bool IsAllowingCalls { get; }

    /// <summary>
    /// Execute an action through the circuit breaker.
    /// Throws CircuitOpenException if circuit is open.
    /// </summary>
    Task<T> Execute<T>(Func<Task<T>> action, CancellationToken ct = default);

    /// <summary>
    /// Execute with automatic fallback if circuit is open.
    /// </summary>
    Task<T> Execute<T>(Func<Task<T>> action, Func<Task<T>> fallback, CancellationToken ct = default);

    /// <summary>
    /// Execute void action through circuit breaker.
    /// </summary>
    Task Execute(Func<Task> action, CancellationToken ct = default);

    /// <summary>
    /// Manually open the circuit. All instances will stop calling.
    /// </summary>
    Task Open();

    /// <summary>
    /// Manually close the circuit. All instances will resume calling.
    /// </summary>
    Task Close();
}

/// <summary>
/// State change notification.
/// </summary>
public record StateChange(string CircuitId, string NewState, DateTimeOffset Timestamp);

/// <summary>
/// Exception thrown when circuit is open and action is blocked.
/// </summary>
public class CircuitOpenException : Exception
{
    public TimeSpan? RetryAfter { get; }
    
    public CircuitOpenException(TimeSpan? retryAfter = null) 
        : base("Circuit is open - calls are being blocked")
    {
        RetryAfter = retryAfter;
    }
}

/// <summary>
/// Wrapper that provides simple interface.
/// </summary>
internal class CircuitBreakerWrapper : ICircuitBreaker
{
    private readonly RedisCircuitBreaker _inner;
    private string _lastKnownState = "Closed";

    public CircuitBreakerWrapper(RedisCircuitBreaker inner)
    {
        _inner = inner;
    }

    public string State => _lastKnownState;
    public bool IsAllowingCalls => _lastKnownState != "Open" && _lastKnownState != "Isolated";

    public async Task<T> Execute<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        try
        {
            var result = await _inner.ExecuteAsync(async _ => await action(), ct);
            _lastKnownState = "Closed";
            return result;
        }
        catch (BrokenCircuitException ex)
        {
            _lastKnownState = "Open";
            throw new CircuitOpenException(ex.RetryAfter);
        }
        catch (IsolatedCircuitException)
        {
            _lastKnownState = "Isolated";
            throw new CircuitOpenException();
        }
    }

    public async Task<T> Execute<T>(Func<Task<T>> action, Func<Task<T>> fallback, CancellationToken ct = default)
    {
        try
        {
            return await Execute(action, ct);
        }
        catch (CircuitOpenException)
        {
            return await fallback();
        }
    }

    public async Task Execute(Func<Task> action, CancellationToken ct = default)
    {
        await Execute(async () => { await action(); return true; }, ct);
    }

    public async Task Open()
    {
        await _inner.IsolateAsync();
        _lastKnownState = "Isolated";
    }

    public async Task Close()
    {
        await _inner.ResetAsync();
        _lastKnownState = "Closed";
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

/// <summary>
/// Dependency Injection extensions.
/// </summary>
public static class CircuitBreakerServiceExtensions
{
    /// <summary>
    /// Add a named circuit breaker to DI.
    /// </summary>
    public static IServiceCollection AddCircuitBreaker(
        this IServiceCollection services,
        string name,
        Action<CircuitBreaker.Builder> configure)
    {
        services.AddKeyedSingleton<ICircuitBreaker>(name, (sp, key) =>
        {
            var builder = new CircuitBreaker.Builder();
            builder.WithCircuitId(name);
            
            var loggerFactory = sp.GetService<ILoggerFactory>();
            if (loggerFactory != null)
            {
                builder.WithLogger(loggerFactory.CreateLogger<RedisCircuitBreaker>());
            }
            
            configure(builder);
            return builder.Build();
        });

        return services;
    }

    /// <summary>
    /// Add multiple circuit breakers from configuration.
    /// </summary>
    public static IServiceCollection AddCircuitBreakers(
        this IServiceCollection services,
        string redisConnection,
        params string[] circuitIds)
    {
        foreach (var id in circuitIds)
        {
            services.AddCircuitBreaker(id, b => b.WithRedis(redisConnection));
        }
        return services;
    }
}
