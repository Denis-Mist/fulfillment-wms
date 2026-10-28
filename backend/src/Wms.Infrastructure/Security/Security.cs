using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Wms.Application.Abstractions;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "fulfillment-wms";
    public string Audience { get; set; } = "fulfillment-wms-frontend";
    public string SigningKey { get; set; } = string.Empty;
    public int LifetimeMinutes { get; set; } = 720;

    public SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(SigningKey));

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = Key(),
        ValidateIssuerSigningKey = true,
        NameClaimType = JwtRegisteredClaimNames.Sub,
        ClockSkew = TimeSpan.FromMinutes(1),
    };
}

internal sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    public string CreateAccessToken(User user)
    {
        var o = options.Value;
        var now = clock.UtcNow.UtcDateTime;
        var token = new JwtSecurityToken(o.Issuer, o.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, user.Id), new Claim(JwtRegisteredClaimNames.Email, user.Email)],
            notBefore: now, expires: now.AddMinutes(o.LifetimeMinutes),
            signingCredentials: new SigningCredentials(o.Key(), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>PBKDF2 из ASP.NET Core Identity.</summary>
internal sealed class IdentityPasswordHasher : IPasswordHasher
{
    private static readonly PasswordHasher<object> Inner = new();
    private static readonly object Subject = new();

    public string Hash(string password) => Inner.HashPassword(Subject, password);

    public bool Verify(string hash, string password) =>
        Inner.VerifyHashedPassword(Subject, hash, password) != PasswordVerificationResult.Failed;
}

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

internal sealed class IdGenerator : IIdGenerator
{
    public string NewSuffix() => Guid.NewGuid().ToString("N")[..12];
}
