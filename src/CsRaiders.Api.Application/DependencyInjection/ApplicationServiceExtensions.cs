using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using Mediator;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Diagnostics;
using CsRaiders.Api.Application.Common.Interfaces;

namespace CsRaiders.Api.Application.DependencyInjection;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddMediator((MediatorOptions options) =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
        });
        services.AddValidatorsFromAssembly(typeof(ApplicationServiceExtensions).Assembly);
        services.AddScoped<IRequestStepTimer, RequestStepTimer>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}

internal sealed class ValidationBehavior<TMessage, TResponse>(
    IEnumerable<IValidator<TMessage>> validators,
    IRequestStepTimer stepTimer)
    : IPipelineBehavior<TMessage, TResponse> where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken ct)
    {
        using var step = stepTimer.Track("ValidationBehavior");
        if (!validators.Any()) return await next(message, ct);
        var context = new ValidationContext<TMessage>(message);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct)));
        var failures = results.SelectMany(r => r.Errors).Where(x => x is not null).ToArray();
        if (failures.Length > 0) throw new ValidationException(failures);
        return await next(message, ct);
    }
}

internal sealed class TransactionBehavior<TMessage, TResponse>(
    IApplicationDbContext db,
    ITransactionManager transactions,
    IRequestStepTimer stepTimer)
    : IPipelineBehavior<TMessage, TResponse> where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken ct)
    {
        // Read-only messages skip the transaction entirely: each statement gets its own
        // implicit snapshot, and SaveChanges has nothing to persist. Commands keep the
        // explicit transaction for multi-step atomicity and for invariants that depend
        // on it (e.g. LoginCommand's pg_advisory_xact_lock and savepoint-retry).
        if (message is IReadOnlyMessage)
            return await next(message, ct);

        using var behavior = stepTimer.Track("TransactionBehavior");
        await using var transaction = await BeginTrackedAsync(ct);
        try
        {
            TResponse response;
            using (stepTimer.Track($"Handler:{typeof(TMessage).Name}"))
                response = await next(message, ct);

            using (stepTimer.Track("SaveChanges"))
                await db.SaveChangesAsync(ct);

            using (stepTimer.Track("CommitTransaction"))
                await transaction.CommitAsync(ct);

            return response;
        }
        catch
        {
            using (stepTimer.Track("RollbackTransaction"))
                await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async ValueTask<ITransactionScope> BeginTrackedAsync(CancellationToken ct)
    {
        using var begin = stepTimer.Track("BeginTransaction");
        return await transactions.BeginAsync(ct);
    }
}
