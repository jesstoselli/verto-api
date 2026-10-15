using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Verto.Application.Abstractions.Auth;
using Verto.Application.Abstractions.Persistence;
using Verto.Application.Common;
using Verto.Application.Dtos.Auth;
using Verto.Domain.Entities;

namespace Verto.Application.UseCases.Auth;

public class AuthService
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwt;
    private readonly AuthOptions _options;
    private readonly IValidator<SignupRequest> _signupValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<RefreshTokenRequest> _refreshValidator;
    private readonly IValidator<LogoutRequest> _logoutValidator;

    public AuthService(
        IApplicationDbContext db,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwt,
        IOptions<AuthOptions> options,
        IValidator<SignupRequest> signupValidator,
        IValidator<LoginRequest> loginValidator,
        IValidator<RefreshTokenRequest> refreshValidator,
        IValidator<LogoutRequest> logoutValidator)
    {
        ArgumentNullException.ThrowIfNull(options);

        _db = db;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _options = options.Value;
        _signupValidator = signupValidator;
        _loginValidator = loginValidator;
        _refreshValidator = refreshValidator;
        _logoutValidator = logoutValidator;
    }

    public async Task<OperationResult<AuthResponse>> SignupAsync(SignupRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.AllowSignup)
        {
            return OperationResult.Fail<AuthResponse>("signup_disabled", "Cadastro desativado.");
        }

        var validation = await _signupValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return OperationResult.Fail<AuthResponse>("validation_error", validation.Errors[0].ErrorMessage);
        }

        var emailNormalized = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == emailNormalized, ct))
        {
            return OperationResult.Fail<AuthResponse>("email_already_registered", "Este email já está cadastrado.");
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = emailNormalized,
            PasswordHash = _passwordHasher.Hash(request.Password),
            DisplayName = request.DisplayName.Trim(),
        };
        _db.Users.Add(user);

        var response = GenerateAndTrackTokens(user);
        await _db.SaveChangesAsync(ct);

        return OperationResult.Ok(response);
    }

    public async Task<OperationResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _loginValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return OperationResult.Fail<AuthResponse>("validation_error", validation.Errors[0].ErrorMessage);
        }

        var emailNormalized = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == emailNormalized, ct);

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            // Same message for unknown email and wrong password: no account enumeration.
            return OperationResult.Fail<AuthResponse>("invalid_credentials", "Email ou senha inválidos.");
        }

        var response = GenerateAndTrackTokens(user);
        await _db.SaveChangesAsync(ct);

        return OperationResult.Ok(response);
    }

    public async Task<OperationResult<AuthResponse>> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _refreshValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return OperationResult.Fail<AuthResponse>("validation_error", validation.Errors[0].ErrorMessage);
        }

        var tokenHash = _jwt.ComputeTokenHash(request.RefreshToken);
        var now = DateTime.UtcNow;

        await using var transaction = await _db.BeginTransactionAsync(ct);

        var presentedToken = await _db.RefreshTokens
            .AsNoTracking()
            .Where(rt => rt.TokenHash == tokenHash && rt.RevokedAt == null && rt.ExpiresAt > now)
            .Select(rt => new { rt.User, rt.FamilyId })
            .SingleOrDefaultAsync(ct);

        if (presentedToken is null)
        {
            return OperationResult.Fail<AuthResponse>("invalid_refresh_token", "Refresh token inválido, revogado ou expirado.");
        }

        // Serializes concurrent refreshes of the same session so only one rotation wins.
        await _db.AcquireRefreshTokenFamilyLockAsync(presentedToken.FamilyId, ct);

        var revokedTokens = await _db.RefreshTokens
            .Where(rt => rt.TokenHash == tokenHash && rt.RevokedAt == null && rt.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(rt => rt.RevokedAt, now), ct);

        if (revokedTokens != 1)
        {
            return OperationResult.Fail<AuthResponse>("invalid_refresh_token", "Refresh token inválido, revogado ou expirado.");
        }

        var response = GenerateAndTrackTokens(presentedToken.User, presentedToken.FamilyId);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return OperationResult.Ok(response);
    }

    public async Task<OperationResult<bool>> LogoutAsync(LogoutRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _logoutValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return OperationResult.Fail<bool>("validation_error", validation.Errors[0].ErrorMessage);
        }

        var tokenHash = _jwt.ComputeTokenHash(request.RefreshToken);
        var now = DateTime.UtcNow;

        await using var transaction = await _db.BeginTransactionAsync(ct);

        // A rotated predecessor still identifies the session, so logging out with it
        // must also revoke any successor already issued.
        var familyId = await _db.RefreshTokens
            .AsNoTracking()
            .Where(rt => rt.TokenHash == tokenHash)
            .Select(rt => (Guid?)rt.FamilyId)
            .SingleOrDefaultAsync(ct);

        if (familyId.HasValue)
        {
            await _db.AcquireRefreshTokenFamilyLockAsync(familyId.Value, ct);
            await _db.RefreshTokens
                .Where(rt => rt.FamilyId == familyId.Value && rt.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(rt => rt.RevokedAt, now), ct);
        }

        await transaction.CommitAsync(ct);

        return OperationResult.Ok(true);
    }

    private AuthResponse GenerateAndTrackTokens(User user, Guid? familyId = null)
    {
        var accessToken = _jwt.GenerateAccessToken(user.Id, user.Email);
        var (refreshToken, refreshHash, refreshExpiresAt) = _jwt.GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            FamilyId = familyId ?? Guid.CreateVersion7(),
            TokenHash = refreshHash,
            ExpiresAt = refreshExpiresAt,
        });

        return new AuthResponse(
            AccessToken: accessToken.Token,
            RefreshToken: refreshToken,
            AccessTokenExpiresAt: accessToken.ExpiresAt,
            RefreshTokenExpiresAt: refreshExpiresAt,
            User: new UserDto(user.Id, user.Email, user.DisplayName));
    }
}
