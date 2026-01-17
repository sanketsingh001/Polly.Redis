// Polly.Redis - Sample Application
// Demonstrates the developer-friendly API

using Polly.Redis;

Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("   Polly.Redis - Distributed Circuit Breaker Demo");
Console.WriteLine("═══════════════════════════════════════════════════════════\n");

// ═══════════════════════════════════════════════════════════════════════════
// EXAMPLE 1: Simplest Usage (2 lines!)
// ═══════════════════════════════════════════════════════════════════════════

Console.WriteLine("📌 Example 1: Basic Usage\n");

var cb = CircuitBreaker.Create("demo-circuit", "localhost:6379");

var requestCount = 0;

// Simulate API calls
for (int i = 1; i <= 15; i++)
{
    try
    {
        var result = await cb.Execute(async () =>
        {
            requestCount++;
            await Task.Delay(50); // Simulate network
            
            // Simulate failures for first 8 calls
            if (requestCount <= 8)
            {
                throw new Exception($"API Error #{requestCount}");
            }
            
            return $"Success #{requestCount}";
        });
        
        Console.WriteLine($"   ✅ Request {i}: {result}");
    }
    catch (CircuitOpenException ex)
    {
        Console.WriteLine($"   ⛔ Request {i}: Circuit OPEN (retry after {ex.RetryAfter?.TotalSeconds:0}s)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   ❌ Request {i}: {ex.Message}");
    }
    
    await Task.Delay(300);
}

await cb.DisposeAsync();

// ═══════════════════════════════════════════════════════════════════════════
// EXAMPLE 2: With Fallback
// ═══════════════════════════════════════════════════════════════════════════

Console.WriteLine("\n\n📌 Example 2: Automatic Fallback\n");

var cb2 = CircuitBreaker.Create("fallback-demo", "localhost:6379");

for (int i = 1; i <= 10; i++)
{
    var result = await cb2.Execute(
        action: async () =>
        {
            await Task.Delay(50);
            if (i <= 6) throw new Exception("Primary failed");
            return "Primary API";
        },
        fallback: async () =>
        {
            await Task.Delay(50);
            return "Fallback API";
        }
    );
    
    Console.WriteLine($"   Request {i}: Used {result}");
    await Task.Delay(200);
}

await cb2.DisposeAsync();

// ═══════════════════════════════════════════════════════════════════════════
// EXAMPLE 3: Advanced Configuration
// ═══════════════════════════════════════════════════════════════════════════

Console.WriteLine("\n\n📌 Example 3: Custom Configuration\n");

var cb3 = CircuitBreaker.Create(config => config
    .WithRedis("localhost:6379")
    .WithCircuitId("advanced-demo")
    .FailWhen(failureRatio: 0.4, minimumCalls: 3)  // 40% failure, min 3 calls
    .StayOpenFor(seconds: 5)                        // Only 5 seconds
    .OnStateChange(change =>
    {
        var emoji = change.NewState switch
        {
            "Opened" => "🔴",
            "Closed" => "🟢",
            "HalfOpen" => "🟡",
            _ => "⚪"
        };
        Console.WriteLine($"   {emoji} State changed to: {change.NewState}");
    })
);

Console.WriteLine("   Sending requests with 40% failure threshold...\n");

for (int i = 1; i <= 8; i++)
{
    try
    {
        await cb3.Execute(async () =>
        {
            await Task.Delay(50);
            if (i <= 4) throw new Exception("Failure");
            return "OK";
        });
        Console.WriteLine($"   ✅ Request {i}: Success");
    }
    catch (CircuitOpenException)
    {
        Console.WriteLine($"   ⛔ Request {i}: Blocked (circuit open)");
    }
    catch
    {
        Console.WriteLine($"   ❌ Request {i}: Failed");
    }
    
    await Task.Delay(300);
}

// Wait for circuit to recover
Console.WriteLine("\n   Waiting 6 seconds for circuit to half-open...\n");
await Task.Delay(6000);

// Try again
for (int i = 9; i <= 12; i++)
{
    try
    {
        await cb3.Execute(async () =>
        {
            await Task.Delay(50);
            return "OK";
        });
        Console.WriteLine($"   ✅ Request {i}: Success (circuit recovered!)");
    }
    catch (CircuitOpenException)
    {
        Console.WriteLine($"   ⛔ Request {i}: Blocked");
    }
    
    await Task.Delay(200);
}

await cb3.DisposeAsync();

// ═══════════════════════════════════════════════════════════════════════════
// EXAMPLE 4: Manual Control
// ═══════════════════════════════════════════════════════════════════════════

Console.WriteLine("\n\n📌 Example 4: Manual Control\n");

var cb4 = CircuitBreaker.Create("manual-demo", "localhost:6379");

Console.WriteLine($"   Current state: {cb4.State}");
Console.WriteLine($"   Is allowing calls: {cb4.IsAllowingCalls}");

Console.WriteLine("\n   Manually opening circuit...");
await cb4.Open();

Console.WriteLine($"   State after Open(): {cb4.State}");
Console.WriteLine($"   Is allowing calls: {cb4.IsAllowingCalls}");

try
{
    await cb4.Execute(async () => "test");
}
catch (CircuitOpenException)
{
    Console.WriteLine("   ⛔ Call blocked as expected");
}

Console.WriteLine("\n   Manually closing circuit...");
await cb4.Close();

Console.WriteLine($"   State after Close(): {cb4.State}");
Console.WriteLine($"   Is allowing calls: {cb4.IsAllowingCalls}");

await cb4.DisposeAsync();

// ═══════════════════════════════════════════════════════════════════════════

Console.WriteLine("\n\n═══════════════════════════════════════════════════════════");
Console.WriteLine("   Demo Complete!");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine(@"
📚 Key Takeaways:

   1. SIMPLE:     CircuitBreaker.Create(""id"", ""redis"")
   2. FALLBACK:   cb.Execute(action, fallback)
   3. CONFIGURE:  CircuitBreaker.Create(config => config.FailWhen(...))
   4. CONTROL:    cb.Open() / cb.Close()
   5. DISTRIBUTED: All instances with same ID share state via Redis

🚀 Your app is now resilient to cascading failures!
");
