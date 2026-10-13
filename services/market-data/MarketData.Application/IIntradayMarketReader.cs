using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Application;

public interface IIntradayMarketReader
{
    public IntradaySnapshot Read();
}
