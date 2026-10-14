using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed record ResearchReport(string Output, TradeProposal? Proposal, LearningLesson? Lesson = null);
