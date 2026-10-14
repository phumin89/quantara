using Quantara.MarketData.Application;
using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Checks;

public sealed class ResearchIntradayReader(IntradaySnapshot snapshot) : IIntradayMarketReader
{
    public IntradaySnapshot Read() => snapshot;
}
