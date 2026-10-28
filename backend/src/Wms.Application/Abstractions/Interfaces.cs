using Wms.Domain.Entities;

namespace Wms.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct);
    Task<User?> FindByIdAsync(string id, CancellationToken ct);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct);
    void Add(User user);
}

public interface IOrganizationRepository
{
    Task<IReadOnlyList<Organization>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken ct);
    void Add(Organization organization);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public interface ITokenService
{
    string CreateAccessToken(User user);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IIdGenerator
{
    /// <summary>Суффикс для идентификаторов вида usr_r_{suffix}.</summary>
    string NewSuffix();
}
