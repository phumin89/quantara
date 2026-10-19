using Mediator;

namespace Quantara.MarketData.Application.News;

public sealed class ReadNewsHandler : IQueryHandler<ReadNewsQuery, NewsPacket>
{
    private readonly INewsReader reader;

    public ReadNewsHandler(INewsReader reader)
    {
        this.reader = reader;
    }

    public ValueTask<NewsPacket> Handle(ReadNewsQuery query, CancellationToken cancellationToken) =>
        new(this.reader.Read(query.Symbols, query.AsOf, cancellationToken));
}
