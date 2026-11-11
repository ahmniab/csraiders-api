using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using Mediator;
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
        services.AddScoped<IPostCommitActions, PostCommitActions>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}

internal sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse> where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken ct)
    {
        if (!validators.Any()) return await next(message, ct);
        var context = new ValidationContext<TMessage>(message);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct)));
        var failures = results.SelectMany(r => r.Errors).Where(x => x is not null).ToArray();
        if (failures.Length > 0) throw new ValidationException(failures);
        return await next(message, ct);
    }
}

internal sealed class TransactionBehavior<TMessage, TResponse>(IApplicationDbContext db, ITransactionManager transactions, IPostCommitActions postCommit)
    : IPipelineBehavior<TMessage, TResponse> where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken ct)
    {
        await using var transaction = await transactions.BeginAsync(ct);
        try
        {
            var response = await next(message, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            postCommit.Run();
            return response;
        }
        catch
        {
            postCommit.Clear();
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}

internal sealed class PostCommitActions : IPostCommitActions
{
    private readonly List<Action> _actions = [];
    public void Enqueue(Action action) => _actions.Add(action);
    public void Run()
    {
        var actions = _actions.ToArray();
        _actions.Clear();
        foreach (var action in actions) action();
    }
    public void Clear() => _actions.Clear();
}
