using Mediator;
using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed class GetPolicyCandidateHandler(
    IReadOnlyTeamRunStore runs,
    ICurrentAccountReader account,
    IReadOnlyCompanyLoop company,
    TimeProvider time
) : IQueryHandler<GetPolicyCandidateQuery, PolicyCandidate?>
{
    public async ValueTask<PolicyCandidate?> Handle(GetPolicyCandidateQuery query, CancellationToken cancellationToken)
    {
        var status = await company.Read(cancellationToken);
        if (!status.Enabled)
        {
            return null;
        }

        var accountKey = await account.Read(cancellationToken);
        var run = await runs.LatestReviewed(accountKey, time.GetUtcNow(), cancellationToken);
        var candidate = run?.PolicyCandidate;
        var reviewer = run?.Steps.LastOrDefault(step => step.Role == TeamRole.Reviewer);
        return
            run?.Status == TeamRunStatus.Completed
            && reviewer?.Status == TeamRunStatus.Completed
            && candidate is { ExpiresAt: var expiresAt }
            && expiresAt > time.GetUtcNow()
            && candidate.AccountKey == accountKey
            && candidate.RunId == run.Id
            ? candidate
            : null;
    }
}
