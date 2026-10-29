using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public sealed class FakeTelegramTransport : ITelegramTransport
{
    public string? Token { get; private set; }

    public int Sends { get; private set; }

    public long LastEditedMessage { get; private set; }

    public string? LastStatus { get; private set; }

    public bool FailSend { get; set; }

    public bool FailEdit { get; set; }

    public long? FailedEditMessage { get; set; }

    public List<long> EditAttempts { get; } = [];

    public List<(string CallbackId, string Text)> Answers { get; } = [];

    public Func<long, CancellationToken, Task<IReadOnlyList<TelegramClick>>>? Polling { get; set; }

    public Action? Editing { get; set; }

    public Action? Answering { get; set; }

    public bool TimeoutEdit { get; set; }

    public bool TimeoutAnswer { get; set; }

    public Task<long> Send(OrderProposal proposal, string token, CancellationToken cancellationToken)
    {
        this.Sends++;
        this.Token = token;
        if (this.FailSend)
        {
            throw new HttpRequestException("Uncertain delivery fixture");
        }

        return Task.FromResult(42L);
    }

    public Task<IReadOnlyList<TelegramClick>> Poll(long offset, CancellationToken cancellationToken) =>
        this.Polling?.Invoke(offset, cancellationToken) ?? Task.FromResult<IReadOnlyList<TelegramClick>>([]);

    public Task Answer(string callbackId, string text, CancellationToken cancellationToken)
    {
        this.Answering?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (this.TimeoutAnswer)
        {
            throw new OperationCanceledException("Callback timeout fixture");
        }

        this.Answers.Add((callbackId, text));
        return Task.CompletedTask;
    }

    public Task Edit(long messageId, OrderProposal proposal, string status, CancellationToken cancellationToken)
    {
        this.EditAttempts.Add(messageId);
        this.Editing?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (this.TimeoutEdit)
        {
            throw new OperationCanceledException("Edit timeout fixture");
        }

        if (this.FailEdit || this.FailedEditMessage == messageId)
        {
            throw new InvalidOperationException("Edit unavailable fixture https://telegram.invalid/botfixture-secret");
        }

        this.LastEditedMessage = messageId;
        this.LastStatus = status;
        return Task.CompletedTask;
    }
}
