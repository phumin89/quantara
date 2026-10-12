namespace Quantara.Portfolio.Application;

public sealed record SyncStatus(bool Syncing, int CompletedSymbols, int TotalSymbols, string? Error);
