namespace Quantara.Portfolio.Domain;

// Derived from matched REST fill fields, before wallet rounding; never evidence of a cash credit.
public sealed record ManualSaleProceedsEstimate(decimal GrossThb, decimal NetFeesThb, decimal UnroundedNetThb);
