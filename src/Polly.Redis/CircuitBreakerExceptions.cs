// Copyright (c) 2015-2024, App vNext (Polly Project)
// Modifications Copyright (c) 2026, Polly.Redis Contributors
// Licensed under BSD-3-Clause

namespace Polly.Redis;

/// <summary>
/// Exception thrown when a circuit is broken.
/// </summary>
/// <remarks>Based on Polly 8.5.0</remarks>
public class BrokenCircuitException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BrokenCircuitException"/> class.
    /// </summary>
    public BrokenCircuitException()
        : base("The circuit is now open and is not allowing calls.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BrokenCircuitException"/> class.
    /// </summary>
    /// <param name="retryAfter">The period after which the circuit will close.</param>
    public BrokenCircuitException(TimeSpan retryAfter)
        : base($"The circuit is now open and is not allowing calls. It can be retried after '{retryAfter}'.")
        => RetryAfter = retryAfter;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrokenCircuitException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="retryAfter">The period after which the circuit will close.</param>
    /// <param name="inner">The inner exception.</param>
    public BrokenCircuitException(string message, TimeSpan retryAfter, Exception? inner = null)
        : base(message, inner) 
        => RetryAfter = retryAfter;

    /// <summary>
    /// Gets the amount of time before the circuit can become closed, if known.
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>
/// Exception thrown when a circuit is manually isolated.
/// </summary>
public class IsolatedCircuitException : BrokenCircuitException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IsolatedCircuitException"/> class.
    /// </summary>
    public IsolatedCircuitException()
        : base("The circuit is manually held open and is not allowing calls.")
    {
    }
}
