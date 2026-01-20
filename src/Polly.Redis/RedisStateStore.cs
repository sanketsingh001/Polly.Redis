using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace CircuitBreaker.Redis.Distributed;

/// <summary>
/// Redis state store for circuit breaker with distributed locking.
/// </summary>
internal class RedisStateStore : IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisCircuitBreakerOptions _options;
    private readonly ILogger _logger;
    private readonly bool _ownsConnection;
    private string? _lockToken;

    public RedisStateStore(
        RedisCircuitBreakerOptions options,
        ILogger logger,
        IConnectionMultiplexer? redis = null)
    {
        _options = options;
        _logger = logger;

        if (redis != null)
        {
            _redis = redis;
            _ownsConnection = false;
        }
        else
        {
            var config = ConfigurationOptions.Parse(_options.RedisConfiguration);
            config.ConnectTimeout = (int)_options.RedisOperationTimeout.TotalMilliseconds;
            config.SyncTimeout = (int)_options.RedisOperationTimeout.TotalMilliseconds;
            // Add resilience
            config.ReconnectRetryPolicy = new LinearRetry(5000);
            config.AbortOnConnectFail = false;

            _redis = ConnectionMultiplexer.Connect(config);
            _ownsConnection = true;

            _logger.LogInformation("Connected to Redis: {Endpoints}", string.Join(", ", config.EndPoints));
        }
    }

    private IDatabase Db => _redis.GetDatabase();
    private string StateKey => $"{_options.KeyPrefix}:{_options.CircuitBreakerId}:state";
    private string MetricsKey => $"{_options.KeyPrefix}:{_options.CircuitBreakerId}:metrics";
    private string BlockedUntilKey => $"{_options.KeyPrefix}:{_options.CircuitBreakerId}:blocked";
    private string LockKey => $"{_options.KeyPrefix}:{_options.CircuitBreakerId}:lock";

    public async Task<CircuitState?> GetStateAsync()
    {
        try
        {
            var value = await Db.StringGetAsync(StateKey);
            return value.IsNullOrEmpty ? null : Enum.Parse<CircuitState>(value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get circuit state");
            if (_options.EnableFallbackToInMemory)
                return null;
            throw;
        }
    }

    public async Task SetStateAsync(CircuitState state)
    {
        try
        {
            await Db.StringSetAsync(StateKey, state.ToString(), TimeSpan.FromHours(24));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set circuit state");
            if (!_options.EnableFallbackToInMemory) throw;
        }
    }

    public async Task<HealthMetrics?> GetMetricsAsync()
    {
        try
        {
            var json = await Db.StringGetAsync(MetricsKey);
            return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<HealthMetrics>(json.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metrics");
            if (_options.EnableFallbackToInMemory)
                return null;
            throw;
        }
    }

    public async Task SetMetricsAsync(HealthMetrics metrics)
    {
        try
        {
            var json = JsonSerializer.Serialize(metrics);
            await Db.StringSetAsync(MetricsKey, json, _options.SamplingDuration + TimeSpan.FromMinutes(1));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set metrics");
            if (!_options.EnableFallbackToInMemory) throw;
        }
    }

    public async Task<DateTimeOffset?> GetBlockedUntilAsync()
    {
        try
        {
            var value = await Db.StringGetAsync(BlockedUntilKey);
            if (value.IsNullOrEmpty) return null;
            return long.TryParse(value.ToString(), out var ticks)
                ? new DateTimeOffset(ticks, TimeSpan.Zero)
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get blocked timestamp");
            if (_options.EnableFallbackToInMemory)
                return null;
            throw;
        }
    }

    public async Task SetBlockedUntilAsync(DateTimeOffset blockedUntil)
    {
        try
        {
            var ttl = blockedUntil - DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1);
            if (ttl > TimeSpan.Zero)
            {
                await Db.StringSetAsync(BlockedUntilKey, blockedUntil.UtcTicks, ttl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set blocked timestamp");
            if (!_options.EnableFallbackToInMemory) throw;
        }
    }

    /// <summary>
    /// Attempts to acquire a distributed lock for state transitions.
    /// CRITICAL: Prevents race conditions when multiple instances try to change state simultaneously.
    /// </summary>
    public async Task<bool> TryAcquireLockAsync(TimeSpan? lockDuration = null)
    {
        try
        {
            lockDuration ??= TimeSpan.FromSeconds(5); // Default: 5 seconds
            _lockToken = Guid.NewGuid().ToString();
            
            // SETNX pattern: SET if Not eXists
            var acquired = await Db.StringSetAsync(LockKey, _lockToken, lockDuration.Value, When.NotExists);
            
            if (acquired)
            {
                _logger.LogDebug("✅ Acquired distributed lock for circuit {CircuitId} (token: {Token})", 
                    _options.CircuitBreakerId, _lockToken.Substring(0, 8));
            }
            else
            {
                _logger.LogDebug("❌ Failed to acquire lock for circuit {CircuitId} - another instance holds it", 
                    _options.CircuitBreakerId);
            }
            
            return acquired;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error acquiring lock for circuit {CircuitId} - failing open", 
                _options.CircuitBreakerId);
            // Fail-open: If we can't acquire lock due to error, allow operation
            // This prevents Redis failures from blocking all state transitions
            return true;
        }
    }

    /// <summary>
    /// Releases the distributed lock using Lua script for atomicity.
    /// Only the lock holder can release their own lock (prevents stealing).
    /// </summary>
    public async Task ReleaseLockAsync()
    {
        if (_lockToken == null)
        {
            return;
        }

        try
        {
            // Lua script ensures atomicity:
            // 1. Check if lock is ours
            // 2. Delete only if it matches
            // This prevents releasing someone else's lock
            var script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                else
                    return 0
                end";

            var result = await Db.ScriptEvaluateAsync(
                script, 
                [LockKey],
                [_lockToken!]);
            
            if ((int)result == 1)
            {
                _logger.LogDebug("🔓 Released distributed lock for circuit {CircuitId}", _options.CircuitBreakerId);
            }
            else
            {
                _logger.LogDebug("⚠️ Lock for circuit {CircuitId} was already released or expired", 
                    _options.CircuitBreakerId);
            }
            
            _lockToken = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error releasing lock for circuit {CircuitId} (will auto-expire in 5s)", 
                _options.CircuitBreakerId);
            // Not critical - lock will expire automatically
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Release any held locks before disposing
        await ReleaseLockAsync();
        
        if (_ownsConnection)
        {
            await _redis.DisposeAsync();
        }
    }
}
