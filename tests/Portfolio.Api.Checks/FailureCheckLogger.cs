using Microsoft.Extensions.Logging;

namespace Portfolio.Api.Checks;

public sealed class FailureCheckLogger : ILogger
{
    public string? Message { get; private set; }

    public Exception? Error { get; private set; }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        this.Message = formatter(state, exception);
        this.Error = exception;
    }
}
