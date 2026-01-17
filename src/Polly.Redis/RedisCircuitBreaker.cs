// Copyright (c) 2015-2024, App vNext (Polly Project)
// Modifications Copyright (c) 2026, Polly.Redis Contributors
// Licensed under BSD-3-Clause
// Based on Polly 8.5.0 CircuitStateController

using Microsoft.Extensions.Logging;

namespace Polly.Redis;

/// <summary>
/// Redis-backed circuit breaker implementation with distributed locking.
/// State is stored in Redis for distributed coordination across instances.
/// </summary>
public class RedisCircuitBreaker : IAsyncDisposable
{
    private readonly RedisCircuitBreakerOptions _options;
    private readonly RedisStateStore _redis;
    private readonly ILogger<RedisCircuitBreaker> _logger;
    
    // Local fallback state (used if Redis is unavailable)
    private CircuitState _localState = CircuitState.Closed;
    private HealthMetrics _localMetrics = new() { WindowStart = DateTimeOffset.UtcNow };
    private DateTimeOffset _localBlockedUntil = DateTimeOffset.MinValue;
    private readonly object _localLock = new();

    public RedisCircuitBreaker(
        RedisCircuitBreakerOptions options,
        ILogger<RedisCircuitBreaker> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _redis = new RedisStateStore(options, logger);
    }

    /// <summary>
    /// Executes an action through the circuit breaker.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        // 1. Check circuit state
        var state = await GetCircuitStateAsync();
        
        if (state == CircuitState.Open)
        {
            var blockedUntil = await GetBlockedUntilAsync();
            
            // Check if break duration has elapsed
            if (blockedUntil.HasValue && DateTimeOffset.UtcNow >= blockedUntil.Value)
            {
                // Transition to HalfOpen (with locking to prevent race)
                await TransitionToHalfOpenAsync();
                state = CircuitState.HalfOpen;
            }
            else
            {
                // Circuit is still open
                var retryAfter = blockedUntil.HasValue
                    ? blockedUntil.Value - DateTimeOffset.UtcNow
                    : _options.BreakDuration;
                
                _logger.LogWarning("Circuit {CircuitId} is OPEN. Rejecting call.", _options.CircuitBreakerId);
                throw new BrokenCircuitException("Circuit is open", retryAfter);
            }
        }

        if (state == CircuitState.Isolated)
        {
            _logger.LogWarning("Circuit {CircuitId} is ISOLATED. Rejecting call.", _options.CircuitBreakerId);
            throw new IsolatedCircuitException();
        }

        // 2. Execute the action
        try
        {
            var result = await action(cancellationToken);
            
            // 3. Record success
            await RecordSuccessAsync(state);
            
            return result;
        }
        catch (Exception ex)
        {
            // 4. Record failure
            await RecordFailureAsync(state, ex);
            throw;
        }
    }

    private async Task<CircuitState> GetCircuitStateAsync()
    {
        var state = await _redis.GetStateAsync();
        
        if (state.HasValue)
        {
            return state.Value;
        }

        // Fallback to local state
        if (_options.EnableFallbackToInMemory)
        {
            lock (_localLock)
            {
                return _localState;
            }
        }

        return CircuitState.Closed; // Default
    }

    private async Task<DateTimeOffset?> GetBlockedUntilAsync()
    {
       var blockedUntil = await _redis.GetBlockedUntilAsync();
        
        if (blockedUntil.HasValue)
        {
            return blockedUntil.Value;
        }

        // Fallback to local
        if (_options.EnableFallbackToInMemory)
        {
            lock (_localLock)
            {
                return _localBlockedUntil == DateTimeOffset.MinValue ? null : _localBlockedUntil;
            }
        }

        return null;
    }

    private async Task RecordSuccessAsync(CircuitState currentState)
    {
        var metrics = await GetOrCreateMetricsAsync();
        metrics = metrics with { SuccessCount = metrics.SuccessCount + 1 };
        await _redis.SetMetricsAsync(metrics);

        // If in HalfOpen, transition to Closed on success (with locking)
        if (currentState == CircuitState.HalfOpen)
        {
            await TransitionToClosedAsync();
        }

        // Update local fallback
        if (_options.EnableFallbackToInMemory)
        {
            lock (_localLock)
            {
                _localMetrics = _localMetrics with { SuccessCount = _localMetrics.SuccessCount + 1 };
            }
        }
    }

    private async Task RecordFailureAsync(CircuitState currentState, Exception ex)
    {
        var metrics = await GetOrCreateMetricsAsync();
        metrics = metrics with { FailureCount = metrics.FailureCount + 1 };
        await _redis.SetMetricsAsync(metrics);

        // Update local fallback
        if (_options.EnableFallbackToInMemory)
        {
            lock (_localLock)
            {
                _localMetrics = _localMetrics with { FailureCount = _localMetrics.FailureCount + 1 };
            }
        }

        // Check if circuit should break (with locking to prevent race)
        if (currentState == CircuitState.HalfOpen)
        {
            // In HalfOpen, any failure reopens the circuit
            await TransitionToOpenAsync(ex);
        }
        else if (currentState == CircuitState.Closed && ShouldBreakCircuit(metrics))
        {
            // Break the circuit if threshold exceeded
            await TransitionToOpenAsync(ex);
        }
    }

    private bool ShouldBreakCircuit(HealthMetrics metrics)
    {
        // Check if window has elapsed
        var windowAge = DateTimeOffset.UtcNow - metrics.WindowStart;
        if (windowAge > _options.SamplingDuration)
        {
            // Window expired, reset metrics
            return false;
        }

        // Check minimum throughput
        if (metrics.TotalCount < _options.MinimumThroughput)
        {
            return false;
        }

        // Check failure ratio
        return metrics.FailureRatio >= _options.FailureRatio;
    }

    private async Task<HealthMetrics> GetOrCreateMetricsAsync()
    {
        var metrics = await _redis.GetMetricsAsync();
        
        if (metrics != null)
        {
            // Check if window has expired
            var windowAge = DateTimeOffset.UtcNow - metrics.WindowStart;
            if (windowAge <= _options.SamplingDuration)
            {
                return metrics;
            }
        }

        // Create new window
        var newMetrics = new HealthMetrics { WindowStart = DateTimeOffset.UtcNow };
        await _redis.SetMetricsAsync(newMetrics);
        return newMetrics;
    }

    /// <summary>
    /// CRITICAL: Uses distributed lock to prevent multiple instances from opening circuit simultaneously.
    /// Without locking, race conditions could cause duplicate "circuit opened" events and inconsistent state.
    /// </summary>
    private async Task TransitionToOpenAsync(Exception? triggeringException = null)
    {
        // Try to acquire distributed lock
        if (!await _redis.TryAcquireLockAsync())
        {
            _logger.LogDebug("Another instance is handling circuit state transition for {CircuitId}", 
                _options.CircuitBreakerId);
            return; // Another instance is handling the transition
        }

        try
        {
            // Double-check state hasn't changed while we were acquiring lock
            var currentState = await _redis.GetStateAsync();
            if (currentState == CircuitState.Open || currentState == CircuitState.Isolated)
            {
                _logger.LogDebug("Circuit {CircuitId} already in {State} - skipping transition", 
                    _options.CircuitBreakerId, currentState);
                return;
            }

            var blockedUntil = DateTimeOffset.UtcNow + _options.BreakDuration;
            
            await _redis.SetStateAsync(CircuitState.Open);
            await _redis.SetBlockedUntilAsync(blockedUntil);

            if (_options.EnableFallbackToInMemory)
            {
                lock (_localLock)
                {
                    _localState = CircuitState.Open;
                    _localBlockedUntil = blockedUntil;
                }
            }

            _logger.LogError(triggeringException, "🔴 Circuit {CircuitId} OPENED. Blocked until {BlockedUntil}",
                _options.CircuitBreakerId, blockedUntil);

            _options.OnCircuitOpened?.Invoke(new CircuitStateChange(
                _options.CircuitBreakerId,
                CircuitState.Closed,
                CircuitState.Open,
                DateTimeOffset.UtcNow,
                triggeringException));
        }
        finally
        {
            // Always release lock
            await _redis.ReleaseLockAsync();
        }
    }

    private async Task TransitionToHalfOpenAsync()
    {
        // Try to acquire lock
        if (!await _redis.TryAcquireLockAsync())
        {
            return; // Another instance is handling it
        }

        try
        {
            // Double-check state
            var currentState = await _redis.GetStateAsync();
            if (currentState != CircuitState.Open)
            {
                return; // State changed
            }

            await _redis.SetStateAsync(CircuitState.HalfOpen);

            if (_options.EnableFallbackToInMemory)
            {
                lock (_localLock)
                {
                    _localState = CircuitState.HalfOpen;
                }
            }

            _logger.LogInformation("🟡 Circuit {CircuitId} transitioned to HALF-OPEN", _options.CircuitBreakerId);

            _options.OnCircuitHalfOpen?.Invoke(new CircuitStateChange(
                _options.CircuitBreakerId,
                CircuitState.Open,
                CircuitState.HalfOpen,
                DateTimeOffset.UtcNow));
        }
        finally
        {
            await _redis.ReleaseLockAsync();
        }
    }

    private async Task TransitionToClosedAsync()
    {
        // Try to acquire lock
        if (!await _redis.TryAcquireLockAsync())
        {
            return; // Another instance is handling it
        }

        try
        {
            await _redis.SetStateAsync(CircuitState.Closed);
            
            // Reset metrics
            await _redis.SetMetricsAsync(new HealthMetrics { WindowStart = DateTimeOffset.UtcNow });

            if (_options.EnableFallbackToInMemory)
            {
                lock (_localLock)
                {
                    _localState = CircuitState.Closed;
                    _localMetrics = new HealthMetrics { WindowStart = DateTimeOffset.UtcNow };
                    _localBlockedUntil = DateTimeOffset.MinValue;
                }
            }

            _logger.LogInformation("🟢 Circuit {CircuitId} transitioned to CLOSED", _options.CircuitBreakerId);

            _options.OnCircuitClosed?.Invoke(new CircuitStateChange(
                _options.CircuitBreakerId,
                CircuitState.HalfOpen,
                CircuitState.Closed,
                DateTimeOffset.UtcNow));
        }
        finally
        {
            await _redis.ReleaseLockAsync();
        }
    }

    /// <summary>
    /// Manually open the circuit (isolate).
    /// </summary>
    public async Task IsolateAsync()
    {
        await _redis.SetStateAsync(CircuitState.Isolated);
        
        if (_options.EnableFallbackToInMemory)
        {
            lock (_localLock)
            {
                _localState = CircuitState.Isolated;
            }
        }

        _logger.LogWarning("⚫ Circuit {CircuitId} manually ISOLATED", _options.CircuitBreakerId);
    }

    /// <summary>
    /// Manually close the circuit (reset).
    /// </summary>
    public async Task ResetAsync()
    {
        await TransitionToClosedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _redis.DisposeAsync();
    }
}
