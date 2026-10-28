using Microsoft.Extensions.Logging;
using Quantara.Portfolio.Application;

namespace Portfolio.Checks;

public sealed class FakeTradingLogger : ILogger<TradingEngine>
{
    public List<string> Messages { get; } = [];

    public bool ExceptionPayloadReceived { get; private set; }

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
        this.Messages.Add(formatter(state, exception));
        this.ExceptionPayloadReceived |= exception is not null;
    }
}
