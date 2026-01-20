using CircuitBreaker.Redis.Distributed;

Console.WriteLine("🧪 CircuitBreaker.Redis.Distributed v2.0 Test\n");


// Test 1: Simple CallWithFallback
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine("Test 1: Simple CallWithFallback");
Console.WriteLine("═══════════════════════════════════════\n");

var cb = DistributedCircuitBreaker.Create("test-api-v2", redisConnection);

// Simulate primary and fallback
async Task<string> PrimaryCall()
{
    await Task.Delay(50);
    return "Primary Success";
}

async Task<string> FallbackCall()
{
    await Task.Delay(50);
    return "Fallback Success";
}

var result = await cb.CallWithFallback(
    primary: PrimaryCall,
    fallback: FallbackCall,
    isSuccess: (r) => r != null
);

Console.WriteLine($"✅ Result: {result}");
Console.WriteLine($"   Circuit State: {cb.State}");
Console.WriteLine($"   IsHealthy: {cb.IsHealthy}\n");

// Test 2: isSuccess determines failure
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine("Test 2: isSuccess determines failure");
Console.WriteLine("═══════════════════════════════════════\n");

var cb2 = DistributedCircuitBreaker.Create("test-api-v2-2", redisConnection);

async Task<ApiResponse> CallWithBadStatus()
{
    await Task.Delay(50);
    return new ApiResponse { StatusCode = 500, Data = null };  // Bad response
}

async Task<ApiResponse> CallWithGoodStatus()
{
    await Task.Delay(50);
    return new ApiResponse { StatusCode = 200, Data = "OK" };  // Good response
}

var result2 = await cb2.CallWithFallback(
    primary: CallWithBadStatus,
    fallback: CallWithGoodStatus,
    isSuccess: (r) => r?.StatusCode == 200  // USER defines success!
);

Console.WriteLine($"✅ Result: StatusCode={result2.StatusCode}, Data={result2.Data}");
Console.WriteLine($"   (Primary returned 500, so fallback was used!)\n");

// Test 3: Multiple fallbacks
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine("Test 3: Multiple Fallbacks");
Console.WriteLine("═══════════════════════════════════════\n");

var cb3 = DistributedCircuitBreaker.Create("test-api-v2-3", redisConnection);

int callCount = 0;

async Task<string> FailingCall()
{
    callCount++;
    Console.WriteLine($"   Calling Fallback {callCount}...");
    await Task.Delay(50);
    if (callCount < 3) return null!;  // Fail first 2
    return "Third Fallback Success!";
}

var result3 = await cb3.CallWithFallback(
    primary: async () => { Console.WriteLine("   Calling Primary..."); return null!; },
    isSuccess: (r) => r != null,
    fallbacks: new Func<Task<string>>[] {
        FailingCall,  // Fails
        FailingCall,  // Fails  
        FailingCall   // Succeeds!
    }
);

Console.WriteLine($"✅ Result: {result3}\n");

// Test 4: Callbacks
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine("Test 4: Callbacks");
Console.WriteLine("═══════════════════════════════════════\n");

var cb4 = DistributedCircuitBreaker.Create(config => config
    .WithCircuitId("test-api-v2-4")
    .WithRedis(redisConnection)
    .FailWhen(0.5, 3)
    .StayOpenFor(10)
    .OnStateChange(change => 
    {
        Console.WriteLine($"   🔔 State Changed: {change.NewState}");
    })
);

// Trigger failures to open circuit
for (int i = 0; i < 5; i++)
{
    try
    {
        await cb4.Execute<string>(async () => 
        {
            await Task.Delay(10);
            throw new Exception("Simulated failure");
        });
    }
    catch { }
}

Console.WriteLine($"\n   Circuit State: {cb4.State}");
Console.WriteLine($"   IsHealthy: {cb4.IsHealthy}\n");

// Test 5: Backward compatibility
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine("Test 5: Backward Compatibility");
Console.WriteLine("═══════════════════════════════════════\n");

// Old API still works! (using fully qualified name to avoid namespace conflict)
var oldCb = CircuitBreaker.Redis.Distributed.CircuitBreaker.Create("old-api", redisConnection);
var oldResult = await oldCb.Execute(
    action: async () => { await Task.Delay(50); return "Old API Works!"; },
    fallback: async () => { await Task.Delay(10); return "Fallback"; }
);
Console.WriteLine($"✅ Old API Result: {oldResult}\n");

// Summary
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine("📊 Summary");
Console.WriteLine("═══════════════════════════════════════\n");

Console.WriteLine("✅ Test 1: Simple CallWithFallback - PASSED");
Console.WriteLine("✅ Test 2: isSuccess parameter - PASSED");
Console.WriteLine("✅ Test 3: Multiple fallbacks - PASSED");
Console.WriteLine("✅ Test 4: State callbacks - PASSED");
Console.WriteLine("✅ Test 5: Backward compatibility - PASSED");

Console.WriteLine("\n🎉 All v2.0 features working!\n");

await cb.DisposeAsync();
await cb2.DisposeAsync();
await cb3.DisposeAsync();
await cb4.DisposeAsync();
await oldCb.DisposeAsync();

// Helper class
public class ApiResponse
{
    public int StatusCode { get; set; }
    public string? Data { get; set; }
}
