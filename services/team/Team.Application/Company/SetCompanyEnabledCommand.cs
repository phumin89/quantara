using Mediator;

namespace Quantara.Team.Application;

public sealed record SetCompanyEnabledCommand(bool Enabled) : ICommand<CompanyLoopOverview>;
