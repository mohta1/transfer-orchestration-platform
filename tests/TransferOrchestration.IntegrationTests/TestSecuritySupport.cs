using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.Tokens;
using TransferOrchestration.Api.Infrastructure.Security;
using TransferOrchestration.BuildingBlocks.Security;

namespace TransferOrchestration.IntegrationTests;

internal static class TestSecurityDefaults
{
    public const string Issuer = "transfer-orchestration-tests";

    public const string Audience = "transfer-orchestration-api";

    public const string SigningKey = "TEST_ONLY_32_BYTE_SIGNING_KEY!!!";

    public static void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseSetting($"Authentication:Jwt:{nameof(JwtOptions.Issuer)}", Issuer);
        builder.UseSetting($"Authentication:Jwt:{nameof(JwtOptions.Audience)}", Audience);
        builder.UseSetting($"Authentication:Jwt:{nameof(JwtOptions.SigningKey)}", SigningKey);
        builder.ConfigureServices(services => services.RemoveHostedWorkers());
    }
}

internal static class TestJwtTokenFactory
{
    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    public static string CreateCustomerToken(
        Guid accountId,
        string subject = "customer-test",
        DateTimeOffset? expires = null) =>
        CreateToken(
            subject,
            [new Claim(ClaimTypes.Role, SecurityClaimTypes.CustomerRole), new Claim(SecurityClaimTypes.AccountId, accountId.ToString("D"))],
            expires ?? DateTimeOffset.UtcNow.AddMinutes(15));

    public static string CreateOperatorToken(
        string subject = "operator-test",
        DateTimeOffset? expires = null) =>
        CreateToken(
            subject,
            [new Claim(ClaimTypes.Role, SecurityClaimTypes.OperatorRole)],
            expires ?? DateTimeOffset.UtcNow.AddMinutes(15));

    public static string CreateCustomerTokenWithIssuer(
        Guid accountId,
        string issuer,
        string subject = "customer-test") =>
        CreateToken(
            subject,
            [new Claim(ClaimTypes.Role, SecurityClaimTypes.CustomerRole), new Claim(SecurityClaimTypes.AccountId, accountId.ToString("D"))],
            DateTimeOffset.UtcNow.AddMinutes(15),
            issuer: issuer);

    public static string CreateCustomerTokenWithAudience(
        Guid accountId,
        string audience,
        string subject = "customer-test") =>
        CreateToken(
            subject,
            [new Claim(ClaimTypes.Role, SecurityClaimTypes.CustomerRole), new Claim(SecurityClaimTypes.AccountId, accountId.ToString("D"))],
            DateTimeOffset.UtcNow.AddMinutes(15),
            audience: audience);

    public static string CreateExpiredCustomerToken(Guid accountId, string subject = "customer-test") =>
        CreateToken(
            subject,
            [new Claim(ClaimTypes.Role, SecurityClaimTypes.CustomerRole), new Claim(SecurityClaimTypes.AccountId, accountId.ToString("D"))],
            DateTimeOffset.UtcNow.AddMinutes(-5));

    public static string CreateCustomerTokenWithWrongSignature(Guid accountId, string subject = "customer-test")
    {
        var wrongKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("WRONG__32_BYTE_SIGNING_KEY!!!!!!"));
        var credentials = new SigningCredentials(wrongKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            TestSecurityDefaults.Issuer,
            TestSecurityDefaults.Audience,
            [new Claim("sub", subject), new Claim(ClaimTypes.Role, SecurityClaimTypes.CustomerRole), new Claim(SecurityClaimTypes.AccountId, accountId.ToString("D"))],
            expires: DateTimeOffset.UtcNow.AddMinutes(15).UtcDateTime,
            signingCredentials: credentials);
        return TokenHandler.WriteToken(token);
    }

    public static string CreateCustomerTokenWithoutAccountClaim(string subject = "customer-test") =>
        CreateToken(
            subject,
            [new Claim(ClaimTypes.Role, SecurityClaimTypes.CustomerRole)],
            DateTimeOffset.UtcNow.AddMinutes(15));

    public static string CreateMalformedToken() => "not-a-valid-jwt-token";

    private static string CreateToken(
        string subject,
        IEnumerable<Claim> claims,
        DateTimeOffset expires,
        string? issuer = null,
        string? audience = null)
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecurityDefaults.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var tokenClaims = new List<Claim> { new("sub", subject) };
        tokenClaims.AddRange(claims);
        var token = new JwtSecurityToken(
            issuer ?? TestSecurityDefaults.Issuer,
            audience ?? TestSecurityDefaults.Audience,
            tokenClaims,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);
        return TokenHandler.WriteToken(token);
    }
}

internal static class TestHttpAuthorization
{
    public static void AuthorizeAsCustomer(this HttpRequestMessage request, Guid accountId, string subject = "customer-test") =>
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokenFactory.CreateCustomerToken(accountId, subject));

    public static void AuthorizeAsOperator(this HttpRequestMessage request, string subject = "operator-test") =>
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokenFactory.CreateOperatorToken(subject));

    public static void AuthorizeAsCustomerWithoutAccountClaim(
        this HttpRequestMessage request,
        string subject = "customer-without-account") =>
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokenFactory.CreateCustomerTokenWithoutAccountClaim(subject));
}
