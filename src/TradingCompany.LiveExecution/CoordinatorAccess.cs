using TradingCompany.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace TradingCompany.LiveExecution;

public sealed class CoordinatorAccessOptions : AuthenticationSchemeOptions
{
    public string Token { get; set; } = "";
    public LiveAccountId AccountId { get; set; }
}
public sealed class CoordinatorAccess(IOptionsMonitor<CoordinatorAccessOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<CoordinatorAccessOptions>(options, logger, encoder)
{
    public const string SchemeName = "QuantaraCoordinator";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization;
        if (Options.AccountId.Value == Guid.Empty || Options.Token is null || Options.Token.Length is < 32 or > 256 ||
            Options.Token.Any(char.IsControl) ||
            header.Count != 1 || header[0] is not { } value || !value.StartsWith("Bearer ", StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());
        var supplied = value[7..];
        if (supplied.Length > 256 || !CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(Options.Token))))
            return Task.FromResult(AuthenticateResult.Fail("Coordinator authentication failed."));
        var claims = new[] { new Claim(ClaimTypes.Role, "Coordinator"),
            new Claim("live-account", Options.AccountId.Value.ToString("N")) };
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
    }
}
