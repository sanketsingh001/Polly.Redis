# End-to-End Test for CircuitBreaker.Redis.Distributed

## 🎯 Purpose

This test verifies the **published NuGet package** works correctly by:
- Installing from NuGet.org (not local project reference)
- Testing all major features
- Simulating multiple instances
- Verifying Redis coordination

## 🚀 Quick Start

### Option 1: With Docker (Recommended)

```powershell
# Start Redis
cd test\EndToEndTest
docker-compose up -d

# Run test
dotnet run

# Stop Redis
docker-compose down
```

### Option 2: With Local Redis

```powershell
# Make sure Redis is running on localhost:6379
redis-server

# Run test
cd test\EndToEndTest
dotnet run
```

### Option 3: Without Redis (Fallback Mode)

```powershell
# Test will use in-memory fallback
cd test\EndToEndTest
dotnet run
```

## ✅ What Gets Tested

1. **Basic Creation** - `CircuitBreaker.Create()`
2. **Successful Calls** - Normal operation
3. **Circuit Breaking** - Automatic opening on failures
4. **Automatic Fallback** - Primary/fallback pattern
5. **Custom Configuration** - Fluent API
6. **Manual Control** - Open/Close methods
7. **Multi-Instance Coordination** - Redis state sharing

## 📊 Expected Output

```
🚀 CircuitBreaker.Redis.Distributed - End-to-End Test

✅ Test 1: Creating circuit breaker...
   Circuit created! State: Closed

✅ Test 2: Executing successful call...
   Result: Success #1

✅ Test 3: Simulating failures to break circuit...
   ⚠️  Failure #1: Simulated failure
   ⚠️  Failure #2: Simulated failure
   ...
   🔴 Circuit OPEN! Retry after: 00:00:30

✅ Test 4: Testing automatic fallback...
   Result: Fallback Success!

✅ Test 5: Custom configuration...
   Custom circuit created!

✅ Test 6: Manual control...
   Initial state: Closed, Allowing calls: True
   After manual open: Isolated, Allowing calls: False
   After manual close: Closed, Allowing calls: True

✅ Test 7: Simulating multiple instances...
   Instance 1 breaking circuit...
   Instance 1 state: Open
   Instance 2 state: Open (should also be Open!)
   ✅ SUCCESS! Both instances coordinated via Redis!

═══════════════════════════════════════════════════
🎉 All Tests Completed!
═══════════════════════════════════════════════════

📦 Package: CircuitBreaker.Redis.Distributed v1.0.0
🔗 NuGet: https://www.nuget.org/packages/CircuitBreaker.Redis.Distributed/
📚 GitHub: https://github.com/sanketsingh001/Polly.Redis

✅ Your package is working perfectly!
```

## 🔧 Troubleshooting

### Redis Connection Failed

If you see connection errors:
1. Make sure Redis is running: `docker-compose ps`
2. Check Redis is accessible: `redis-cli ping`
3. Verify port 6379 is not in use: `netstat -an | findstr 6379`

### Package Not Found

If NuGet can't find the package:
1. Wait 5-10 minutes after publishing
2. Clear NuGet cache: `dotnet nuget locals all --clear`
3. Check package exists: https://www.nuget.org/packages/CircuitBreaker.Redis.Distributed/

## 📝 Notes

- This test uses the **published NuGet package**, not the local project
- Redis is optional - package falls back to in-memory if unavailable
- Test takes ~5 seconds to run
- All resources are properly disposed

## 🎉 Success Criteria

✅ All 7 tests pass  
✅ No exceptions thrown  
✅ Multi-instance coordination works  
✅ Package installs from NuGet.org  
