namespace YowThi.Erp.Application.Common.Idempotency;

public enum CommandExecutionAcquireStatus
{
    Acquired,
    Replay,
    Conflict,
}
