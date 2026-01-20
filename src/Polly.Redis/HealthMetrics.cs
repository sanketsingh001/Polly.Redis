namespace CircuitBreaker.Redis.Distributed;

/// <summary>
/// Health metrics for circuit breaker with sliding window.
/// </summary>
public record HealthMetrics
{
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public DateTimeOffset WindowStart { get; init; }
    public int TotalCount => SuccessCount + FailureCount;
    public double FailureRatio => TotalCount == 0 ? 0.0 : (double)FailureCount / TotalCount;
}
