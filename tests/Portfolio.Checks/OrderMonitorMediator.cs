using Mediator;
using Quantara.Portfolio.Application;

namespace Portfolio.Checks;

public sealed class OrderMonitorMediator : IMediator
{
    public Func<ReconcileOrderCommand, CancellationToken, OrderView>? Reconcile { get; set; }

    public ValueTask<TResponse> Send<TResponse>(
        ICommand<TResponse> command,
        CancellationToken cancellationToken = default
    )
    {
        if (
            command is not ReconcileOrderCommand reconcile
            || this.Reconcile is null
            || this.Reconcile(reconcile, cancellationToken) is not TResponse response
        )
        {
            throw new InvalidOperationException("The monitor may dispatch only status reconciliation.");
        }

        return ValueTask.FromResult(response);
    }

    public ValueTask<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default
    ) => throw new InvalidOperationException("Unexpected monitor request.");

    public ValueTask<TResponse> Send<TResponse>(
        IQuery<TResponse> query,
        CancellationToken cancellationToken = default
    ) => throw new InvalidOperationException("Unexpected monitor query.");

    public ValueTask<object?> Send(object message, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Unexpected monitor message.");

    public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => throw new InvalidOperationException("Unexpected monitor notification.");

    public ValueTask Publish(object notification, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Unexpected monitor notification.");

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamQuery<TResponse> query,
        CancellationToken cancellationToken = default
    ) => throw new InvalidOperationException("Unexpected monitor stream.");

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default
    ) => throw new InvalidOperationException("Unexpected monitor stream.");

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamCommand<TResponse> command,
        CancellationToken cancellationToken = default
    ) => throw new InvalidOperationException("Unexpected monitor stream.");

    public IAsyncEnumerable<object?> CreateStream(object message, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Unexpected monitor stream.");
}
