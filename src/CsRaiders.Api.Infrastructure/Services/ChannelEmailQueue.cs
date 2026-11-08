using System.Threading.Channels;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CsRaiders.Api.Infrastructure.Services;

public sealed class ChannelEmailQueue(ILogger<ChannelEmailQueue> logger) : IEmailQueue
{
    private readonly Channel<EmailMessage> channel = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    public bool TryEnqueue(EmailMessage message)
    {
        var accepted = channel.Writer.TryWrite(message);
        if (!accepted) logger.LogWarning("Email queue is full; dropping message for redacted recipient.");
        return accepted;
    }
    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken ct = default) => channel.Reader.ReadAllAsync(ct);
}
