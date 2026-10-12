namespace Quantara.Portfolio.Domain;

public sealed record Balance(string Symbol, decimal Quantity, decimal? Available = null);
