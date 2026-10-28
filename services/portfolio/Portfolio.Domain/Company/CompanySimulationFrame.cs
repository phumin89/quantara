using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Domain.Company;

public sealed record CompanySimulationFrame(DateTimeOffset At, IReadOnlyList<TradingBook> Books);
