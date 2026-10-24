using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Tests.Platform;

// The Accounts side of the platform-role path, without a database: what the Platform facade is allowed to see of it.
internal sealed class FakeAccountRoles : IAccountRoles
{
    private readonly Dictionary<string, AccountId> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<AccountId> _holders = [];

    public Error? FailWith { get; set; }

    public IReadOnlyCollection<AccountId> Holders => _holders;

    public AccountId AddAccount(string email, bool holdsRole = false)
    {
        var account = new AccountId(Guid.NewGuid());
        _accounts[email.Trim()] = account;
        if (holdsRole)
        {
            _holders.Add(account);
        }

        return account;
    }

    public Task<AccountId?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.TryGetValue((email ?? string.Empty).Trim(), out var account) ? account : (AccountId?)null);

    public Task<Result<bool>> GrantPlatformAdminAsync(
        AccountId account,
        AuditActor actor,
        string reason,
        CancellationToken cancellationToken) =>
        Task.FromResult(FailWith is { } error ? Result<bool>.Failure(error) : Result<bool>.Success(_holders.Add(account)));

    public Task<Result<bool>> RevokePlatformAdminAsync(
        AccountId account,
        AuditActor actor,
        string reason,
        CancellationToken cancellationToken) =>
        Task.FromResult(FailWith is { } error ? Result<bool>.Failure(error) : Result<bool>.Success(_holders.Remove(account)));

    public Task<Result<IReadOnlyList<AccountId>>> ListPlatformAdminsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<AccountId>>.Success([.. _holders]));
}
