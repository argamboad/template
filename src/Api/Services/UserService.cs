using Template.Core.Entities;
using Template.Core.Repositories;
using Template.Infrastructure.Persistence;

namespace Template.Api.Services;

/// <summary>
/// Service for user management (creation, lookup, login linking).
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Resolves the account for an OAuth identity.
    /// Lookup order: exact provider login → email match (links the new provider to
    /// the existing account) → create a new account (with a fresh tenant).
    /// </summary>
    // emailVerified defaults to false (fail-closed) — a caller that forgets the flag
    // must NOT silently bypass the takeover guard.
    Task<User> GetOrCreateUserAsync(string email, string providerUserId, string provider,
        string? displayName = null, bool emailVerified = false);

    /// <summary>
    /// Attaches an OAuth identity to an existing account (explicit linking —
    /// email match not required).
    /// </summary>
    Task<LinkLoginResult> LinkLoginAsync(Guid userId, string provider, string providerUserId);

    /// <summary>
    /// Resolves the account for a verified email (passwordless sign-in via magic
    /// link or OTP). Creates a fresh account + tenant when none exists; no external
    /// login row is attached. Email ownership is proven by the redemption, so the
    /// account is marked email-verified.
    /// </summary>
    Task<User> GetOrCreateByEmailAsync(string email, string? displayName = null);

    /// <summary>Gets a user by ID.</summary>
    Task<User?> GetUserByIdAsync(Guid userId);
}

/// <summary>
/// An unverified email claim matched an existing account — refusing the merge
/// blocks the credential-attachment takeover.
/// </summary>
public class UnverifiedEmailConflictException(string email)
    : Exception($"Email '{email}' matches an existing account but the provider did not verify it");

public enum LinkLoginResult
{
    Linked,
    AlreadyLinkedToSameAccount,
    OwnedByAnotherAccount
}

public class UserService(IUserRepository repository, AppDbContext db, ILogger<UserService> logger) : IUserService
{
    public async Task<User> GetOrCreateUserAsync(string email, string providerUserId, string provider,
        string? displayName = null, bool emailVerified = false)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be empty", nameof(email));
        if (string.IsNullOrWhiteSpace(providerUserId))
            throw new ArgumentException("Provider user ID cannot be empty", nameof(providerUserId));
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider cannot be empty", nameof(provider));

        // Normalize email so the same address in different casing resolves to one
        // account — stored and matched lower-cased.
        email = email.Trim().ToLowerInvariant();

        // 1. Known provider identity → existing account
        var existingUser = await repository.GetByLoginAsync(provider, providerUserId);
        if (existingUser != null)
        {
            await RefreshDisplayNameAsync(existingUser, displayName);
            logger.LogInformation("User found by login: {Email} (provider: {Provider})", email, provider);
            return existingUser;
        }

        // 2. Same email from a new provider → same account; link the identity.
        // Guard: an UNVERIFIED email claim must never attach a new credential to an
        // existing account (takeover vector) — refuse outright.
        var userByEmail = await repository.GetByEmailAsync(email);
        if (userByEmail != null && !emailVerified)
        {
            logger.LogWarning("Refused unverified-email merge for {Email} via {Provider}", email, provider);
            throw new UnverifiedEmailConflictException(email);
        }
        if (userByEmail != null)
        {
            await repository.AddLoginAsync(new UserLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = userByEmail.Id,
                Provider = provider,
                ProviderUserId = providerUserId
            });
            await RefreshDisplayNameAsync(userByEmail, displayName);

            logger.LogInformation("Linked {Provider} login to existing account: {Email} (userId: {UserId})",
                provider, email, userByEmail.Id);
            return userByEmail;
        }

        // 3. Brand new user — create the tenant ("household of one"), the user, and an
        // owner membership linking them, atomically.
        var trimmedName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        var newUser = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            DisplayName = trimmedName,
            EmailVerified = emailVerified
        };
        newUser.Logins.Add(new UserLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = newUser.Id,
            Provider = provider,
            ProviderUserId = providerUserId
        });

        var createdUser = await CreateUserWithTenantAsync(newUser, trimmedName);

        logger.LogInformation("New user created: {Email} (provider: {Provider}, userId: {UserId})",
            email, provider, createdUser.Id);

        return createdUser;
    }

    public async Task<User> GetOrCreateByEmailAsync(string email, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be empty", nameof(email));

        email = email.Trim().ToLowerInvariant();

        var existing = await repository.GetByEmailAsync(email);
        if (existing != null)
        {
            await RefreshDisplayNameAsync(existing, displayName);
            return existing;
        }

        var trimmedName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        var newUser = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            DisplayName = trimmedName,
            EmailVerified = true // ownership proven by redeeming the link/code
        };

        var created = await CreateUserWithTenantAsync(newUser, trimmedName);
        logger.LogInformation("New passwordless user created: {Email} (userId: {UserId})", email, created.Id);
        return created;
    }

    /// <summary>
    /// Creates a brand-new user together with a fresh "household of one" tenant and
    /// an owner membership linking them. All three rows are written in one
    /// SaveChanges so a half-provisioned account can never persist.
    /// </summary>
    private async Task<User> CreateUserWithTenantAsync(User newUser, string? trimmedName)
    {
        var now = DateTimeOffset.UtcNow;
        var tenantLabel = trimmedName is { Length: > 0 }
            ? trimmedName.Split(' ')[0]
            : newUser.Email.Split('@')[0];

        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            Name = $"{tenantLabel}'s Household",
            CreatedAt = now,
            UpdatedAt = now
        };
        newUser.CreatedAt = now;
        newUser.UpdatedAt = now;

        var membership = new TenantMembership
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            UserId = newUser.Id,
            Role = TenantRoles.Owner,
            JoinedAt = now
        };

        db.Tenants.Add(tenant);
        db.Users.Add(newUser);
        db.TenantMemberships.Add(membership);
        await db.SaveChangesAsync();
        return newUser;
    }

    public Task<User?> GetUserByIdAsync(Guid userId) => repository.GetByIdAsync(userId);

    public async Task<LinkLoginResult> LinkLoginAsync(Guid userId, string provider, string providerUserId)
    {
        var owner = await repository.GetByLoginAsync(provider, providerUserId);
        if (owner != null)
        {
            return owner.Id == userId
                ? LinkLoginResult.AlreadyLinkedToSameAccount
                : LinkLoginResult.OwnedByAnotherAccount;
        }

        await repository.AddLoginAsync(new UserLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Provider = provider,
            ProviderUserId = providerUserId
        });

        logger.LogInformation("Explicitly linked {Provider} login to user {UserId}", provider, userId);
        return LinkLoginResult.Linked;
    }

    private async Task RefreshDisplayNameAsync(User user, string? displayName)
    {
        // A provided name refreshes the stored one; null/blank never erases it.
        var trimmed = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        if (trimmed != null && trimmed != user.DisplayName)
        {
            user.DisplayName = trimmed;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await repository.UpdateAsync(user);
        }
    }
}
