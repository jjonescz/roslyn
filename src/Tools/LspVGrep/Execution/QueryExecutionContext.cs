using LspVGrepTool.Infrastructure;

namespace LspVGrepTool.Execution;

internal sealed class QueryExecutionContext : IDisposable
{
    private readonly RoslynWorkspaceProvider _workspaceProvider;
    private readonly ExternalSearchRunner _externalSearchRunner;
    private Task<WorkspaceLoadResult>? _workspaceLoadTask;

    public QueryExecutionContext(
        string directoryPath,
        bool useLscache,
        RoslynWorkspaceProvider workspaceProvider,
        ExternalSearchRunner externalSearchRunner)
    {
        DirectoryPath = directoryPath;
        UseLscache = useLscache;
        _workspaceProvider = workspaceProvider;
        _externalSearchRunner = externalSearchRunner;
    }

    public string DirectoryPath { get; }

    public bool UseLscache { get; }

    public async Task<WorkspaceLoadResult> GetWorkspaceAsync(CancellationToken cancellationToken)
    {
        _workspaceLoadTask ??= _workspaceProvider.LoadAsync(DirectoryPath, UseLscache, cancellationToken);
        return await _workspaceLoadTask;
    }

    public Task<ExternalSearchResult> SearchTypeDefinitionPwshAsync(string typeName, CancellationToken cancellationToken) =>
        _externalSearchRunner.SearchTypeDefinitionPwshAsync(DirectoryPath, typeName, cancellationToken);

    public Task<ExternalSearchResult> SearchTypeNamePwshAsync(string typeName, CancellationToken cancellationToken) =>
        _externalSearchRunner.SearchTypeNamePwshAsync(DirectoryPath, typeName, cancellationToken);

    public Task<ExternalSearchResult> SearchImplementationPwshAsync(string typeName, CancellationToken cancellationToken) =>
        _externalSearchRunner.SearchImplementationPwshAsync(DirectoryPath, typeName, cancellationToken);

    public Task<ExternalSearchResult> SearchDerivedTypesPwshAsync(string typeName, CancellationToken cancellationToken) =>
        _externalSearchRunner.SearchDerivedTypesPwshAsync(DirectoryPath, typeName, cancellationToken);

    public Task<ExternalSearchResult> SearchMemberDefinitionPwshAsync(string memberName, CancellationToken cancellationToken) =>
        _externalSearchRunner.SearchMemberDefinitionPwshAsync(DirectoryPath, memberName, cancellationToken);

    public WorkspaceLoadResult? TryGetLoadedWorkspace()
    {
        return _workspaceLoadTask is { IsCompletedSuccessfully: true }
            ? _workspaceLoadTask.Result
            : null;
    }

    public void Dispose()
    {
        _workspaceProvider.Dispose();
    }
}
