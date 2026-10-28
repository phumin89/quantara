using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Portfolio.Checks;

public sealed class OrderMonitorStore(IReadOnlyList<OrderProposal> proposals) : IReadOnlyOrderStore
{
    public IReadOnlyList<OrderProposal> Proposals { get; set; } = proposals;

    public Task<IReadOnlyList<OrderProposal>> List(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OrderProposal>>(this.Proposals.Take(50).ToArray());

    public Task<IReadOnlyList<OrderProposal>> Exposure(DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(this.Proposals);

    public Task<OrderProposal?> Find(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(this.Proposals.FirstOrDefault(proposal => proposal.Id == id));
}
