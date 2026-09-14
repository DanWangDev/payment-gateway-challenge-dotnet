using Microsoft.Extensions.Logging;

namespace PaymentGateway.Api.Tests;

/// <summary>
/// Captures messages and structured fields without a logging provider or scope infrastructure.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> fields
            ? fields.ToDictionary(field => field.Key, field => field.Value)
            : new Dictionary<string, object?>();
        Entries.Add(new LogEntry(logLevel, formatter(state, exception), properties, exception));
    }
}

internal sealed record LogEntry(
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> Properties,
    Exception? Exception);
