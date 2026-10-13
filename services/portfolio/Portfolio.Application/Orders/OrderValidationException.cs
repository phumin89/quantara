namespace Quantara.Portfolio.Application;

public sealed class OrderValidationException(string message) : Exception(message);
