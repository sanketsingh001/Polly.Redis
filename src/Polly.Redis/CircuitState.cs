// Copyright (c) 2015-2024, App vNext (Polly Project)
// Modifications Copyright (c) 2026, CircuitBreaker.Redis.Distributed Contributors
// Licensed under BSD-3-Clause

namespace CircuitBreaker.Redis.Distributed;

/// <summary>
/// Describes the possible states the circuit of a Circuit Breaker may be in.
/// </summary>
/// <remarks>Based on Polly 8.5.0</remarks>
public enum CircuitState
{
    /// <summary>
    /// Closed - When the circuit is closed. Execution of actions is allowed.
    /// </summary>
    Closed,

    /// <summary>
    /// Open - When the automated controller has opened the circuit. Execution of actions is blocked.
    /// </summary>
    Open,

    /// <summary>
    /// Half-open - When the circuit is recovering from an open state.
    /// </summary>
    HalfOpen,

    /// <summary>
    /// Isolated - When the circuit has been manually opened. Execution is blocked until reset.
    /// </summary>
    Isolated
}
