// CircuitBreaker.Redis.Distributed - v2.0
// Simple, powerful distributed circuit breaker with Redis coordination
// All instances share state - when one breaks, ALL stop calling!

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace CircuitBreaker.Redis.Distributed;

#region Simple API - Start Here!

/// <summary>
/// 🚀 QUICK START:
/// 
/// var cb = DistributedCircuitBreaker.Create("my-api", "redis:6379");
/// 
/// // Simple call
/// var result = await cb.Execute(() => CallApi());
/// 
/// // With automatic fallback - NO EXCEPTIONS NEEDED!
/// var result = await cb.CallWithFallback(
///     primary: () => CallPrimaryApi(),
///     fallback: () => CallBackupApi(),
///     isSuccess: (r) => r?.StatusCode == 200
/// );
/// 
/// // Multiple fallbacks (try each in order)
/// var result = await cb.CallWithFallback(
///     primary: () => AzureApi(),
///     isSuccess: (r) => r != null,
///     fallbacks: new[] { () => AwsApi(), () => CachedData() }
/// );
/// </summary>
public static class DistributedCircuitBreaker
{
    /// <summary>
    /// Create circuit breaker with minimal config.
    /// </summary>
    /// <param name="circuitId">Unique ID - instances with same ID share state via Redis</param>
    /// <param name="redisConnection">Redis connection (Azure, AWS, self-hosted)</param>
    public static IDistributedCircuitBreaker Create(string circuitId, string redisConnection)
    {
        return new Builder()
            .WithCircuitId(circuitId)
            .WithRedis(redisConnection)
            .Build();
    }

    /// <summary>
    /// Create with full configuration.
    /// </summary>
    public static IDistributedCircuitBreaker Create(Action<Builder> configure)
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
        private Action<CircuitStateChange>? _onStateChange;
        private Action? _onCircuitOpen;
        private Action? _onCircuitClose;
        private ILogger<RedisCircuitBreaker>? _logger;

        /// <summary>Set circuit ID. Instances with same ID share state.</summary>
        public Builder WithCircuitId(string id) { _circuitId = id; return this; }

        /// <summary>Set Redis connection. Works with Azure, AWS, Redis Cloud, self-hosted.</summary>
        public Builder WithRedis(string connectionString) { _redis = connectionString; return this; }

        /// <summary>Configure failure threshold.</summary>
        /// <param name="failureRatio">Break when failures exceed this ratio (0.0-1.0). Default: 0.5</param>
        /// <param name="minimumCalls">Minimum calls before circuit can break. Default: 5</param>
        public Builder FailWhen(double failureRatio = 0.5, int minimumCalls = 5)
        {
            _failureRatio = failureRatio;
            _minimumCalls = minimumCalls;
            return this;
        }

        /// <summary>How long circuit stays open before retrying. Default: 30s</summary>
        public Builder StayOpenFor(TimeSpan duration) { _breakDuration = duration; return this; }
        
        /// <summary>How long circuit stays open (seconds).</summary>
        public Builder StayOpenFor(int seconds) { _breakDuration = TimeSpan.FromSeconds(seconds); return this; }

        /// <summary>Sliding window for failure measurement. Default: 10s</summary>
        public Builder MeasureFailuresOver(TimeSpan window) { _samplingWindow = window; return this; }

        /// <summary>Called when circuit state changes.</summary>
        public Builder OnStateChange(Action<CircuitStateChange> handler) { _onStateChange = handler; return this; }

        /// <summary>Called when circuit opens (starts blocking calls).</summary>
        public Builder OnCircuitOpen(Action handler) { _onCircuitOpen = handler; return this; }

        /// <summary>Called when circuit closes (resumes calls).</summary>
        public Builder OnCircuitClose(Action handler) { _onCircuitClose = handler; return this; }

        /// <summary>If Redis unavailable, fallback to local memory. Default: true</summary>
        public Builder FallbackToMemory(bool enable = true) { _fallbackToMemory = enable; return this; }

        /// <summary>Provide logger for debugging.</summary>
        public Builder WithLogger(ILogger<RedisCircuitBreaker> logger) { _logger = logger; return this; }

        public IDistributedCircuitBreaker Build()
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
                OnCircuitOpened = _onStateChange != null || _onCircuitOpen != null
                    ? c => { _onStateChange?.Invoke(new CircuitStateChange(c.CircuitBreakerId, "Open", c.Timestamp)); _onCircuitOpen?.Invoke(); }
                    : null,
                OnCircuitClosed = _onStateChange != null || _onCircuitClose != null
                    ? c => { _onStateChange?.Invoke(new CircuitStateChange(c.CircuitBreakerId, "Closed", c.Timestamp)); _onCircuitClose?.Invoke(); }
                    : null,
                OnCircuitHalfOpen = _onStateChange != null
                    ? c => _onStateChange(new CircuitStateChange(c.CircuitBreakerId, "HalfOpen", c.Timestamp))
                    : null
            };

            var logger = _logger ?? NullLogger<RedisCircuitBreaker>.Instance;
            var innerCb = new RedisCircuitBreaker(options, logger);
            return new DistributedCircuitBreakerWrapper(innerCb);
        }
    }
}

#endregion

#region Interface

/// <summary>
/// Distributed circuit breaker interface.
/// </summary>
public interface IDistributedCircuitBreaker : IAsyncDisposable
{
    /// <summary>Current state: Closed, Open, HalfOpen, Isolated</summary>
    string State { get; }

    /// <summary>Is circuit allowing calls?</summary>
    bool IsAllowingCalls { get; }

    /// <summary>Is circuit healthy (closed)?</summary>
    bool IsHealthy { get; }

    /// <summary>
    /// Execute action through circuit breaker.
    /// Throws CircuitOpenException if circuit is open.
    /// </summary>
    Task<T> Execute<T>(Func<Task<T>> action, CancellationToken ct = default);

    /// <summary>
    /// 🆕 v2.0: Simple call with automatic fallback - NO EXCEPTIONS!
    /// 
    /// Example:
    /// var result = await cb.CallWithFallback(
    ///     primary: () => CallPrimaryApi(),
    ///     fallback: () => CallBackupApi(),
    ///     isSuccess: (r) => r?.StatusCode == 200
    /// );
    /// </summary>
    /// <param name="primary">Primary call to make</param>
    /// <param name="fallback">Fallback if primary fails or circuit is open</param>
    /// <param name="isSuccess">YOUR definition of success - return true if response is good</param>
    /// <param name="onCircuitOpen">Optional: called when circuit opens</param>
    /// <param name="onFallbackUsed">Optional: called when fallback is used</param>
    Task<T> CallWithFallback<T>(
        Func<Task<T>> primary,
        Func<Task<T>> fallback,
        Func<T, bool> isSuccess,
        Action? onCircuitOpen = null,
        Action? onFallbackUsed = null);

    /// <summary>
    /// 🆕 v2.0: Call with multiple fallbacks (tried in order).
    /// 
    /// Example:
    /// var result = await cb.CallWithFallback(
    ///     primary: () => AzureApi(),
    ///     isSuccess: (r) => r != null,
    ///     fallbacks: new[] { () => AwsApi(), () => GcpApi(), () => CachedData() }
    /// );
    /// </summary>
    Task<T> CallWithFallback<T>(
        Func<Task<T>> primary,
        Func<T, bool> isSuccess,
        Func<Task<T>>[] fallbacks,
        Action? onCircuitOpen = null,
        Action? onFallbackUsed = null);

    /// <summary>Execute with automatic fallback (old API, kept for compatibility).</summary>
    Task<T> Execute<T>(Func<Task<T>> action, Func<Task<T>> fallback, CancellationToken ct = default);

    /// <summary>Execute void action.</summary>
    Task Execute(Func<Task> action, CancellationToken ct = default);

    /// <summary>Manually open circuit (blocks all calls across all instances).</summary>
    Task Open();

    /// <summary>Manually close circuit (resumes calls across all instances).</summary>
    Task Close();
}

#endregion

#region DTOs

/// <summary>Circuit state change notification.</summary>
public record CircuitStateChange(string CircuitId, string NewState, DateTimeOffset Timestamp);

/// <summary>Exception when circuit is open.</summary>
public class CircuitOpenException : Exception
{
    public TimeSpan? RetryAfter { get; }
    public CircuitOpenException(TimeSpan? retryAfter = null)
        : base("Circuit is open - calls are being blocked") => RetryAfter = retryAfter;
}

#endregion

#region Implementation

/// <summary>
/// Wrapper providing simple interface over RedisCircuitBreaker.
/// </summary>
internal class DistributedCircuitBreakerWrapper : IDistributedCircuitBreaker
{
    private readonly RedisCircuitBreaker _inner;
    private string _lastKnownState = "Closed";

    public DistributedCircuitBreakerWrapper(RedisCircuitBreaker inner) => _inner = inner;

    public string State => _lastKnownState;
    public bool IsAllowingCalls => _lastKnownState != "Open" && _lastKnownState != "Isolated";
    public bool IsHealthy => _lastKnownState == "Closed";

    public async Task<T> Execute<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        try
        {
            var result = await _inner.ExecuteAsync(async _ => await action(), ct);
            _lastKnownState = "Closed";
            return result;
        }
        catch (IsolatedCircuitException)
        {
            _lastKnownState = "Isolated";
            throw new CircuitOpenException();
        }
        catch (BrokenCircuitException ex)
        {
            _lastKnownState = "Open";
            throw new CircuitOpenException(ex.RetryAfter);
        }
    }

    /// <summary>
    /// 🆕 v2.0: Simple API with NO EXCEPTIONS!
    /// </summary>
    public async Task<T> CallWithFallback<T>(
        Func<Task<T>> primary,
        Func<Task<T>> fallback,
        Func<T, bool> isSuccess,
        Action? onCircuitOpen = null,
        Action? onFallbackUsed = null)
    {
        // If circuit is open, go straight to fallback
        if (_lastKnownState == "Open" || _lastKnownState == "Isolated")
        {
            onCircuitOpen?.Invoke();
            onFallbackUsed?.Invoke();
            return await fallback();
        }

        try
        {
            // Execute through circuit breaker
            var result = await _inner.ExecuteAsync(async _ => await primary(), default);
            
            // Check if successful using USER'S criteria
            if (isSuccess(result))
            {
                _lastKnownState = "Closed";
                return result;
            }
            else
            {
                // Response received but not successful - use fallback
                onFallbackUsed?.Invoke();
                return await fallback();
            }
        }
        catch (IsolatedCircuitException)
        {
            _lastKnownState = "Isolated";
            onCircuitOpen?.Invoke();
            onFallbackUsed?.Invoke();
            return await fallback();
        }
        catch (BrokenCircuitException)
        {
            _lastKnownState = "Open";
            onCircuitOpen?.Invoke();
            onFallbackUsed?.Invoke();
            return await fallback();
        }
        catch (Exception)
        {
            // Any exception = failure, use fallback
            onFallbackUsed?.Invoke();
            return await fallback();
        }
    }

    /// <summary>
    /// 🆕 v2.0: Multiple fallbacks - tries each in order until one succeeds.
    /// </summary>
    public async Task<T> CallWithFallback<T>(
        Func<Task<T>> primary,
        Func<T, bool> isSuccess,
        Func<Task<T>>[] fallbacks,
        Action? onCircuitOpen = null,
        Action? onFallbackUsed = null)
    {
        // First try primary
        if (_lastKnownState != "Open" && _lastKnownState != "Isolated")
        {
            try
            {
                var result = await _inner.ExecuteAsync(async _ => await primary(), default);
                if (isSuccess(result))
                {
                    _lastKnownState = "Closed";
                    return result;
                }
            }
            catch (IsolatedCircuitException)
            {
                _lastKnownState = "Isolated";
                onCircuitOpen?.Invoke();
            }
            catch (BrokenCircuitException)
            {
                _lastKnownState = "Open";
                onCircuitOpen?.Invoke();
            }
            catch { /* Fall through to fallbacks */ }
        }
        else
        {
            onCircuitOpen?.Invoke();
        }

        // Try each fallback in order
        onFallbackUsed?.Invoke();
        
        foreach (var fallback in fallbacks)
        {
            try
            {
                var result = await fallback();
                if (isSuccess(result))
                {
                    return result;
                }
            }
            catch
            {
                // Try next fallback
            }
        }

        // All fallbacks failed - return last attempt or throw
        return await fallbacks[^1]();
    }

    // Old API - kept for backward compatibility
    public async Task<T> Execute<T>(Func<Task<T>> action, Func<Task<T>> fallback, CancellationToken ct = default)
    {
        try { return await Execute(action, ct); }
        catch (CircuitOpenException) { return await fallback(); }
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

#endregion

#region DI Extensions

/// <summary>
/// Dependency Injection extensions for ASP.NET Core.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add named circuit breaker to DI.
    /// 
    /// Usage:
    /// services.AddDistributedCircuitBreaker("payment", b => b.WithRedis("redis:6379"));
    /// 
    /// Inject:
    /// public MyService([FromKeyedServices("payment")] IDistributedCircuitBreaker cb)
    /// </summary>
    public static IServiceCollection AddDistributedCircuitBreaker(
        this IServiceCollection services,
        string name,
        Action<DistributedCircuitBreaker.Builder> configure)
    {
        services.AddKeyedSingleton<IDistributedCircuitBreaker>(name, (sp, key) =>
        {
            var builder = new DistributedCircuitBreaker.Builder();
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
    /// Add multiple circuit breakers with same Redis connection.
    /// </summary>
    public static IServiceCollection AddDistributedCircuitBreakers(
        this IServiceCollection services,
        string redisConnection,
        params string[] circuitIds)
    {
        foreach (var id in circuitIds)
        {
            services.AddDistributedCircuitBreaker(id, b => b.WithRedis(redisConnection));
        }
        return services;
    }
}

#endregion

#region Backward Compatibility (Old names still work)

// Old interface name - kept for backward compatibility
public interface ICircuitBreaker : IDistributedCircuitBreaker { }

// Old static class name - kept for backward compatibility
public static class CircuitBreaker
{
    public static IDistributedCircuitBreaker Create(string circuitId, string redis)
        => DistributedCircuitBreaker.Create(circuitId, redis);

    public static IDistributedCircuitBreaker Create(Action<DistributedCircuitBreaker.Builder> configure)
        => DistributedCircuitBreaker.Create(configure);
}

// Old DI extension names
public static class CircuitBreakerServiceExtensions
{
    public static IServiceCollection AddCircuitBreaker(
        this IServiceCollection services,
        string name,
        Action<DistributedCircuitBreaker.Builder> configure)
        => services.AddDistributedCircuitBreaker(name, configure);

    public static IServiceCollection AddCircuitBreakers(
        this IServiceCollection services,
        string redisConnection,
        params string[] circuitIds)
        => services.AddDistributedCircuitBreakers(redisConnection, circuitIds);
}

#endregion
