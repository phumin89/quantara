namespace Quantara.Portfolio.Domain;

public sealed record Coin(string Symbol, string Name, string Source = "exchange");
