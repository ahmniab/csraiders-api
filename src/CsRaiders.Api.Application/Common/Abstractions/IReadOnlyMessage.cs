namespace CsRaiders.Api.Application.Common.Abstractions;

/// <summary>
/// Marks a mediator message as read-only. <c>TransactionBehavior</c> skips the
/// explicit transaction and <c>SaveChanges</c> for such messages: PostgreSQL gives
/// every statement its own implicit snapshot, so single-SELECT queries need no
/// explicit transaction, and an empty <c>SaveChanges</c> is a pure overhead.
/// Only mark messages whose handlers never persist through the EF DbContext
/// (external side effects such as IDistributedCache writes are unrelated).
/// </summary>
public interface IReadOnlyMessage
{
}
