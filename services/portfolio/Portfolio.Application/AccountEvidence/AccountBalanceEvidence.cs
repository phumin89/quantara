namespace Quantara.Portfolio.Application;

public sealed record AccountBalanceEvidence(string Symbol, decimal Quantity, decimal? Available);
