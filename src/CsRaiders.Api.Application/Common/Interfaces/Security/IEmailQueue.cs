using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public interface IEmailQueue
{
    bool TryEnqueue(EmailMessage message);
    IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken ct = default);
}
