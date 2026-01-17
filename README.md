# Polly.Redis

[![NuGet](https://img.shields.io/nuget/v/Polly.Redis.svg)](https://www.nuget.org/packages/Polly.Redis/)
[![License](https://img.shields.io/badge/license-BSD--3--Clause-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

**Distributed Circuit Breaker for .NET** - Coordinate circuit breaker state across multiple instances using Redis.

## 🚀 Quick Start

```bash
dotnet add package Polly.Redis
```

```csharp
using Polly.Redis;

// Create
var cb = CircuitBreaker.Create("my-api", "localhost:6379");

// Use
var result = await cb.Execute(async () => await CallApi());
```

## ✨ Features

- 🌐 **Distributed State** - All instances coordinate via Redis
- ⚡ **Zero Config** - Works out of the box
- 🔄 **Universal Redis** - Azure, AWS, Redis Cloud, self-hosted
- 🛡️ **Resilient** - Fallback to local state if Redis unavailable
- 📊 **Smart Metrics** - Sliding window failure tracking
- 🎯 **Simple API** - Easy to use, hard to misuse

## 📖 More Examples

### With Automatic Fallback
```csharp
var result = await cb.Execute(
    action: async () => await PrimaryApi(),
    fallback: async () => await BackupApi()
);
```

### Custom Configuration
```csharp
var cb = CircuitBreaker.Create(config => config
    .WithRedis("your-redis:6379")
    .WithCircuitId("payment-api")
    .FailWhen(failureRatio: 0.5, minimumCalls: 5)
    .StayOpenFor(seconds: 30)
    .OnStateChange(change => logger.Log(change))
);
```

### Dependency Injection
```csharp
builder.Services.AddCircuitBreaker("payment-api", config => config
    .WithRedis(builder.Configuration.GetConnectionString("Redis"))
    .FailWhen(0.5, 5)
);
```

## 📊 How It Works

When any instance detects failures exceeding threshold:
1. Circuit breaks in Redis
2. **ALL instances** immediately see it
3. All stop calling the failing service
4. After break duration, circuit half-opens
5. If service recovered, all resume

**Perfect for microservices, cloud apps, and distributed systems!**

## 🎯 Use Cases

- 🏢 **Microservices** - Coordinate circuit state across services
- ☁️ **Cloud Apps** - Azure, AWS, GCP deployments
- 📈 **Scaled Apps** - Kubernetes, Docker Swarm
- 🔄 **API Gateways** - Protect downstream services
- 💰 **Cost Savings** - Stop hammering paid APIs when they're down

## 🆚 Why Choose Polly.Redis?

| Feature | Polly Built-in | Polly.Redis |
|---------|----------------|-------------|
| State Storage | In-memory (per instance) | Redis (distributed) |
| Multi-instance Coordination | ❌ No | ✅ Yes |
| All instances know circuit state | ❌ No | ✅ Yes |
| Manual failover across instances | ❌ No | ✅ Yes |
| Works with any Redis | N/A | ✅ Yes |

## 📁 Documentation

- [Quick Start Guide](src/Polly.Redis/README.md)
- [Sample Application](samples/Polly.Redis.Sample/)
- API Reference (coming soon)

## 🤝 Contributing

Contributions welcome! Please:
1. Fork the repository
2. Create a feature branch
3. Add tests
4. Submit a Pull Request

## 📄 License

BSD-3-Clause (same as Polly)

Based on [Polly](https://github.com/App-vNext/Polly) circuit breaker concepts.

---

**Built with** ❤️ **by** [Sanket Singh](https://github.com/sanketsingh001)
