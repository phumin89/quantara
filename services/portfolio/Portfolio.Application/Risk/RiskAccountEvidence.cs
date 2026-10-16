using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public sealed record RiskAccountEvidence(
    DateTimeOffset ObservedAt,
    Balance[] Balances,
    RiskOpenOrder[] OpenOrders,
    CapitalCashFlow[] CashFlows,
    DateTimeOffset CashFlowCoverageFrom
);
