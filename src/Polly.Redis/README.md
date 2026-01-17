# Polly.Redis

**Distributed Circuit Breaker for .NET** - Simple, Configurable, Production-Ready

## 🚀 Quick Start

### Install
```bash
dotnet add package Polly.Redis
```

### Simplest Usage (2 lines!)
```csharp
using Polly.Redis;

// Create
var cb = CircuitBreaker.Create("my-api", "localhost:6379");

// Use
var result = await cb.Execute(async () => await CallExternalApi());
```

**That's it!** All instances sharing the same circuit ID coordinate automatically.

---

## 📖 Usage Examples

### Example 1: Basic Protection
```csharp
var cb = CircuitBreaker.Create("payment-api", "redis:6379");

try
{
    var result = await cb.Execute(async () => await PaymentService.Charge(100));
}
catch (CircuitOpenException ex)
{
    // Service is unhealthy, circuit is open
    Console.WriteLine($"Service down. Retry after: {ex.RetryAfter}");
}
```

### Example 2: Automatic Fallback
```csharp
var cb = CircuitBreaker.Create("weather-api", "redis:6379");

// Tries primary, uses fallback if circuit is open
var weather = await cb.Execute(
    action: async () => await FastWeatherApi.Get(),
    fallback: async () => await SlowButStableApi.Get()
);
```

### Example 3: Custom Configuration
```csharp
var cb = CircuitBreaker.Create(config => config
    .WithRedis("your-redis:6379")
    .WithCircuitId("inventory-service")
    .FailWhen(failureRatio: 0.3, minimumCalls: 10)  // Break at 30% failure
    .StayOpenFor(seconds: 60)                        // Stay open for 1 minute
    .MeasureFailuresOver(TimeSpan.FromSeconds(30))   // 30s sliding window
    .OnStateChange(change => 
    {
        Console.WriteLine($"Circuit {change.CircuitId} is now {change.NewState}");
        // Send alert, update dashboard, etc.
    })
);
```

### Example 4: Dependency Injection
```csharp
// Program.cs
builder.Services.AddCircuitBreaker("payment-api", config => config
    .WithRedis(builder.Configuration.GetConnectionString("Redis"))
    .FailWhen(failureRatio: 0.5, minimumCalls: 5)
    .StayOpenFor(seconds: 30)
);

// In your service
public class PaymentService
{
    private readonly ICircuitBreaker _cb;
    
    public PaymentService([FromKeyedServices("payment-api")] ICircuitBreaker cb)
    {
        _cb = cb;
    }
    
    public async Task<Receipt> ProcessPayment(decimal amount)
    {
        return await _cb.Execute(async () => 
        {
            return await externalPaymentGateway.Charge(amount);
        });
    }
}
```

### Example 5: Multiple Circuit Breakers
```csharp
// Register multiple circuits
builder.Services.AddCircuitBreakers(
    redisConnection: "redis:6379",
    "payment-api",
    "inventory-api",
    "shipping-api"
);

// Use by name
public class OrderService
{
    private readonly ICircuitBreaker _paymentCb;
    private readonly ICircuitBreaker _inventoryCb;
    
    public OrderService(
        [FromKeyedServices("payment-api")] ICircuitBreaker paymentCb,
        [FromKeyedServices("inventory-api")] ICircuitBreaker inventoryCb)
    {
        _paymentCb = paymentCb;
        _inventoryCb = inventoryCb;
    }
}
```

### Example 6: Manual Control
```csharp
var cb = CircuitBreaker.Create("my-api", "redis:6379");

// Check state
if (!cb.IsAllowingCalls)
{
    Console.WriteLine("Circuit is open or isolated");
}

// Manually open (maintenance mode)
await cb.Open();  // All instances stop calling

// Manually close (resume)
await cb.Close(); // All instances resume
```

---

## ⚙️ Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `WithCircuitId(string)` | "default" | Unique ID - instances with same ID share state |
| `WithRedis(string)` | "localhost:6379" | Redis connection (any provider) |
| `FailWhen(ratio, minCalls)` | 0.5, 5 | Break when failures > ratio after minCalls |
| `StayOpenFor(duration)` | 30s | How long circuit stays open |
| `MeasureFailuresOver(window)` | 10s | Sliding window for failure measurement |
| `FallbackToMemory(bool)` | true | Use local state if Redis unavailable |
| `OnStateChange(handler)` | null | Callback when state changes |
| `WithLogger(logger)` | Console | Logger for debugging |

---

## 🔄 How It Works

```
Normal Operation                When Failures Exceed Threshold
─────────────────               ───────────────────────────────
App Instance 1 ──→ External API  Instance 1 ──✗ BLOCKED
App Instance 2 ──→ External API  Instance 2 ──✗ BLOCKED
App Instance 3 ──→ External API  Instance 3 ──✗ BLOCKED
       ↑                                ↑
       └── All calls go through         └── Circuit OPEN in Redis
                                            (all instances see it)

After Break Duration              If Recovery Succeeds
────────────────────              ─────────────────────
Circuit: HALF-OPEN                Circuit: CLOSED
One probe request allowed         All instances resume
If fails → Back to OPEN           Normal operation
If succeeds → CLOSED
```

---

## 🌐 Works With Any Redis

```csharp
// Local Redis
.WithRedis("localhost:6379")

// Azure Redis Cache
.WithRedis("your-cache.redis.cache.windows.net:6380,ssl=True,password=key")

// AWS ElastiCache
.WithRedis("your-cluster.cache.amazonaws.com:6379")

// Redis Cloud
.WithRedis("redis-12345.cloud.redislabs.com:12345,password=pwd")

// Redis Cluster
.WithRedis("node1:6379,node2:6379,node3:6379")
```

---

## 📊 State Reference

| State | Meaning | Calls Allowed |
|-------|---------|---------------|
| **Closed** | Normal operation | ✅ Yes |
| **Open** | Too many failures, blocking calls | ❌ No |
| **HalfOpen** | Testing if service recovered | ⚠️ One probe |
| **Isolated** | Manually opened | ❌ No |

---

## 📄 License

BSD-3-Clause (same as Polly)

Based on [Polly](https://github.com/App-vNext/Polly) circuit breaker concepts.
