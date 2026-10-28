namespace Quantara.Portfolio.Domain;

public sealed record ManualSaleAllocation(ManualSaleLot Lot, ManualSaleExecution Execution, decimal Quantity);
