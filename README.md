# CircuitBreaker.Redis.Distributed

[![NuGet](https://img.shields.io/nuget/v/CircuitBreaker.Redis.Distributed.svg)](https://www.nuget.org/packages/CircuitBreaker.Redis.Distributed/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/CircuitBreaker.Redis.Distributed.svg)](https://www.nuget.org/packages/CircuitBreaker.Redis.Distributed/)
[![License](https://img.shields.io/badge/license-BSD--3--Clause-blue.svg)](LICENSE)

**Distributed Circuit Breaker for .NET that coordinates state across multiple application instances using Redis.**

When one instance detects failures and breaks the circuit, **ALL instances immediately stop calling** the failing service. No more cascading failures!

## 🚀 Quick Start

```bash
dotnet add package CircuitBreaker.Redis.Distributed
```

```csharp
using CircuitBreaker.Redis.Distributed;

// Create circuit breaker
var cb = DistributedCircuitBreaker.Create("payment-api", "localhost:6379");

// Simple call with automatic fallback - NO EXCEPTIONS!
var result = await cb.CallWithFallback(
    primary: () => CallPaymentGateway(amount),
    fallback: () => CallBackupGateway(amount),
    isSuccess: (r) => r?.Success == true
);
```

**That's it!** 3 lines of code for distributed resilience.

---

## 🎯 Why This Package?

### The Problem

You have 10 instances of your payment service. When the payment gateway goes down:

```
Instance 1: Detects failure, waits, retries, fails again...
Instance 2: Doesn't know, keeps calling, fails...
Instance 3: Doesn't know, keeps calling, fails...
...
Instance 10: All failing, users frustrated, cascading failure!
```

**Recovery time: 25+ minutes** (each instance learns independently)

### The Solution

With CircuitBreaker.Redis.Distributed:

```
Instance 1: Detects failures → Opens circuit in Redis
Instance 2: Checks Redis → Sees circuit open → Uses fallback
Instance 3: Checks Redis → Sees circuit open → Uses fallback
...
All instances: Immediate fallback, users happy!
```

**Recovery time: 10 seconds** (coordinated via Redis)

---

## ✨ Features

| Feature | Description |
|---------|-------------|
| **🌐 Distributed State** | All instances share circuit state via Redis |
| **🔄 Multiple Fallbacks** | Chain fallbacks that try in order |
| **✅ Custom Success Check** | YOU define what "success" means |
| **🔒 Distributed Locking** | Prevents race conditions on state changes |
| **📊 Sliding Window** | Only counts recent failures (configurable) |
| **🔌 Works Everywhere** | Azure Redis, AWS ElastiCache, Redis Cloud, self-hosted |
| **💾 Auto Fallback** | Works even if Redis is down (uses local memory) |
| **📡 State Callbacks** | Get notified when circuit opens/closes |

---

## 📖 Usage Examples

### Simple Call with Fallback

```csharp
var result = await cb.CallWithFallback(
    primary: () => CallPrimaryApi(),
    fallback: () => CallBackupApi(),
    isSuccess: (r) => r?.StatusCode == 200
);
// result is ALWAYS valid (from primary or fallback)
// No try-catch needed!
```

### Multiple Fallbacks

```csharp
var result = await cb.CallWithFallback(
    primary: () => CallAzureApi(),
    isSuccess: (r) => r?.Data != null,
    fallbacks: new[] {
        () => CallAwsApi(),        // Try this first
        () => CallGcpApi(),        // Then this
        () => GetCachedData()      // Last resort
    }
);
```

### With Logging

```csharp
var result = await cb.CallWithFallback(
    primary: () => CallExternalService(),
    fallback: () => GetCachedResponse(),
    isSuccess: (r) => r != null,
    onCircuitOpen: () => logger.LogWarning("Circuit opened!"),
    onFallbackUsed: () => logger.LogInfo("Using fallback")
);
```

### Full Configuration

```csharp
var cb = DistributedCircuitBreaker.Create(config => config
    .WithCircuitId("payment-gateway")
    .WithRedis("your-cache.redis.cache.windows.net:6380,ssl=True,password=xxx")
    .FailWhen(failureRatio: 0.5, minimumCalls: 5)
    .StayOpenFor(TimeSpan.FromSeconds(30))
    .MeasureFailuresOver(TimeSpan.FromSeconds(10))
    .OnStateChange(change => 
    {
        logger.LogWarning($"Circuit {change.CircuitId} → {change.NewState}");
    })
);
```

### ASP.NET Core Dependency Injection

```csharp
// Program.cs
builder.Services.AddDistributedCircuitBreaker("payment", b => b
    .WithRedis(connectionString)
    .FailWhen(0.5, 5)
);

// PaymentService.cs
public class PaymentService
{
    private readonly IDistributedCircuitBreaker _cb;

    public PaymentService(
        [FromKeyedServices("payment")] IDistributedCircuitBreaker cb)
    {
        _cb = cb;
    }

    public async Task<PaymentResult> ProcessPayment(decimal amount)
    {
        return await _cb.CallWithFallback(
            primary: () => _primaryGateway.Charge(amount),
            fallback: () => _backupGateway.Charge(amount),
            isSuccess: (r) => r?.Approved == true
        );
    }
}
```

---

## 🔧 How It Works

### State Machine

```
┌──────────────────────────────────────────────────┐
│                                                  │
│    CLOSED ────(failures exceed threshold)────►   │
│       │                                OPEN      │
│       │                                  │       │
│       ◄──(probe succeeds)── HALF-OPEN ◄──┘       │
│                                  │               │
│                     (probe fails)────────────►   │
│                                                  │
└──────────────────────────────────────────────────┘
```

| State | Allows Calls? | Description |
|-------|---------------|-------------|
| **Closed** | ✅ Yes | Normal operation |
| **Open** | ❌ No | Blocking calls, using fallback |
| **Half-Open** | ⚠️ One | Testing if service recovered |

### Redis Keys Structure

```
cb:{circuitId}:state     → "Closed" | "Open" | "HalfOpen"
cb:{circuitId}:metrics   → { successCount, failureCount, windowStart }
cb:{circuitId}:blocked   → Timestamp when circuit opened
cb:{circuitId}:lock      → Distributed lock token
```

---

## ⚙️ Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `CircuitId` | Required | Unique ID - instances with same ID share state |
| `RedisConnection` | Required | Redis connection string |
| `FailureRatio` | 0.5 (50%) | Break when failures exceed this ratio |
| `MinimumCalls` | 5 | Min calls before circuit can break |
| `BreakDuration` | 30s | How long circuit stays open |
| `SamplingWindow` | 10s | Sliding window for failure tracking |
| `FallbackToMemory` | true | Use local memory if Redis unavailable |

---

## 🌐 Supported Redis Providers

```csharp
// Azure Redis Cache
.WithRedis("your-cache.redis.cache.windows.net:6380,ssl=True,password=xxx")

// AWS ElastiCache
.WithRedis("your-cluster.amazonaws.com:6379")

// Redis Cloud
.WithRedis("redis-12345.cloud.redislabs.com:12345,password=xxx")

// Self-hosted / Docker
.WithRedis("localhost:6379")
```

---

## 📊 Performance

Tested with Azure Redis Cache:

| Metric | Value |
|--------|-------|
| **Throughput** | 460+ requests/second |
| **Average Latency** | 104ms |
| **P99 Latency** | 213ms |
| **Recovery Time** | 10 seconds (vs 25 min without) |

---

## 🔌 Real-World Scenarios

### Scenario 1: Payment Gateway Failover

```csharp
// Primary: Stripe, Fallback: PayPal
var payment = await cb.CallWithFallback(
    primary: () => _stripe.Charge(amount),
    fallback: () => _paypal.Charge(amount),
    isSuccess: (r) => r?.Approved == true,
    onFallbackUsed: () => _metrics.IncrementPaypalUsage()
);
```

### Scenario 2: Database Read Replica

```csharp
// Primary: Read/Write DB, Fallback: Read Replica
var user = await cb.CallWithFallback(
    primary: () => _primaryDb.GetUser(id),
    fallback: () => _replicaDb.GetUser(id),
    isSuccess: (r) => r != null
);
```

### Scenario 3: External API with Cache

```csharp
// Primary: Live API, Fallback: Cached data
var data = await cb.CallWithFallback(
    primary: () => _externalApi.GetData(),
    fallback: () => _cache.GetData(),
    isSuccess: (r) => r?.IsValid == true
);
```

### Scenario 4: Multi-Region Failover

```csharp
var response = await cb.CallWithFallback(
    primary: () => CallUsEastApi(),
    isSuccess: (r) => r?.Success == true,
    fallbacks: new[] {
        () => CallUsWestApi(),
        () => CallEuApi(),
        () => GetCachedResponse()
    }
);
```

---

## 📋 API Reference

### IDistributedCircuitBreaker

```csharp
public interface IDistributedCircuitBreaker
{
    // Properties
    string State { get; }           // Current state
    bool IsAllowingCalls { get; }   // Can calls go through?
    bool IsHealthy { get; }         // Is circuit closed?

    // Simple call with fallback (v2.0)
    Task<T> CallWithFallback<T>(
        Func<Task<T>> primary,
        Func<Task<T>> fallback,
        Func<T, bool> isSuccess,
        Action? onCircuitOpen = null,
        Action? onFallbackUsed = null);

    // Multiple fallbacks (v2.0)
    Task<T> CallWithFallback<T>(
        Func<Task<T>> primary,
        Func<T, bool> isSuccess,
        Func<Task<T>>[] fallbacks,
        Action? onCircuitOpen = null,
        Action? onFallbackUsed = null);

    // Manual control
    Task Open();    // Block all calls
    Task Close();   // Resume calls
}
```

---

## 🔄 Migration from v1.x

v2.0 is **fully backward compatible**. Your existing code works!

```csharp
// v1.x code (still works)
using Polly.Redis;  // Old namespace still works
var cb = CircuitBreaker.Create("api", "redis");

// v2.0 code (recommended)
using CircuitBreaker.Redis.Distributed;  // New namespace
var cb = DistributedCircuitBreaker.Create("api", "redis");
```

**New in v2.0:**
- `CallWithFallback` - Simple API, no exceptions needed
- `isSuccess` - YOU define what success means
- Multiple fallbacks
- Better namespace

---

## 📚 Learn More

- [Complete Feature List](docs/features.md)
- [Internal Architecture](docs/architecture.md)
- [Sliding Window Algorithm](docs/sliding-window.md)
- [Distributed Locking](docs/locking.md)

---

## 🤝 Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md).

---

## 📄 License

BSD-3-Clause License. See [LICENSE](LICENSE) for details.

---

## 🙏 Acknowledgments

- Inspired by [Polly](https://github.com/App-vNext/Polly) circuit breaker patterns
- Uses [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) for Redis operations

---

**Made with ❤️ by [Sanket Singh](https://github.com/sanketsingh001)**
