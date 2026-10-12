namespace Quantara.Portfolio.Infrastructure;

public sealed class ExchangeReadException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}
