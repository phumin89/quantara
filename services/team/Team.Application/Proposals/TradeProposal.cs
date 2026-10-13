using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed record TradeProposal(
    string Symbol,
    TradeProposalSide Side,
    string Amount,
    string LimitPrice,
    string Reason,
    DateTimeOffset EvidenceAt
);
