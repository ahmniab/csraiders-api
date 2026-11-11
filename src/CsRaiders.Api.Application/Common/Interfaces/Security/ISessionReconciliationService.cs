namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public interface ISessionReconciliationService
{
    /// <summary>Reduces a user to one active session after loss of session:multi.</summary>
    Task ReconcileAfterMultiSessionLossAsync(Guid userId, CancellationToken ct = default);
}
