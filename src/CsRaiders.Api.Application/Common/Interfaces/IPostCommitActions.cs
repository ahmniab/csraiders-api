namespace CsRaiders.Api.Application.Common.Interfaces;

/// <summary>Defers externally visible side effects until the command transaction commits.</summary>
public interface IPostCommitActions
{
    void Enqueue(Action action);
    void Run();
    void Clear();
}
