using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CircuitBreaker.Redis.Distributed;

/// <summary>
/// Extension methods for easy circuit breaker usage.
/// </summary>
public static class RedisCircuitBreakerExtensions
{
    /// <summary>
    /// Registers a Redis circuit breaker in the service collection.
    /// </summary>
    public static IServiceCollection AddRedisCircuitBreaker(
        this IServiceCollection services,
        Action<RedisCircuitBreakerOptions> configure)
    {
        var options = new RedisCircuitBreakerOptions();
        configure(options);

        services.AddSingleton(options);
        services.AddSingleton<RedisCircuitBreaker>();

        return services;
    }

    /// <summary>
    /// Creates a Redis circuit breaker with the given options.
    /// </summary>
    public static RedisCircuitBreaker CreateRedisCircuitBreaker(
        this RedisCircuitBreakerOptions options,
        ILogger<RedisCircuitBreaker>? logger = null)
    {
        logger ??= NullLogger<RedisCircuitBreaker>.Instance;

        return new RedisCircuitBreaker(options, logger);
    }

    /// <summary>
    /// Quick setup: Create circuit breaker with connection string and circuit ID.
    /// </summary>
    public static RedisCircuitBreaker CreateRedisCircuitBreaker(
        string redisConnectionString,
        string circuitBreakerId,
        ILogger<RedisCircuitBreaker>? logger = null)
    {
        var options = new RedisCircuitBreakerOptions
        {
            RedisConfiguration = redisConnectionString,
            CircuitBreakerId = circuitBreakerId
        };

        return options.CreateRedisCircuitBreaker(logger);
    }
}
