namespace Quantara.Portfolio.Application;

public enum TradingFailureStage
{
    Reconciliation,
    MarketScan,
    MarketRead,
    Valuation,
    PaperFill,
    ExitStudy,
    SignalEvaluation,
    LiveExecution,
    AutonomySnapshot,
    BrokerMarket,
    ProposalValidation,
    BrokerBalance,
    PositionOwnership,
    AvailableFunds,
    SubmissionPreparation,
    SubmissionClaim,
    FinalSubmissionGuard,
}
