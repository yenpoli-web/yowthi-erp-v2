namespace YowThi.Erp.Application.Common.Outbox;

public interface IOutboxWriter
{
    ValueTask EnqueueAsync(
        OutboxMessageDraft message,
        CancellationToken cancellationToken);
}
