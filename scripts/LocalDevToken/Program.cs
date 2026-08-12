using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

if (args.Any(arg => arg is "--help" or "-h"))
{
    PrintUsage();
    return 0;
}

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var role = GetRequiredOption(args, "--role").ToLowerInvariant();
var subject = GetOptionalOption(args, "--sub") ?? (role == "operator" ? "local-operator" : "local-customer");
var signingKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")
    ?? throw new InvalidOperationException("JWT_SIGNING_KEY environment variable is required (minimum 32 characters).");
if (signingKey.Length < 32)
{
    throw new InvalidOperationException("JWT_SIGNING_KEY must be at least 32 characters.");
}

var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "transfer-orchestration";
var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "transfer-orchestration-api";
var lifetimeMinutes = int.TryParse(Environment.GetEnvironmentVariable("JWT_LIFETIME_MINUTES"), out var minutes)
    ? minutes
    : 15;

var claims = new List<Claim> { new("sub", subject) };
switch (role)
{
    case "customer":
        claims.Add(new Claim(ClaimTypes.Role, "customer"));
        var accountId = GetRequiredOption(args, "--account-id");
        if (!Guid.TryParse(accountId, out _))
        {
            throw new InvalidOperationException("--account-id must be a GUID.");
        }

        claims.Add(new Claim("account_id", accountId));
        break;
    case "operator":
        claims.Add(new Claim(ClaimTypes.Role, "operator"));
        break;
    default:
        throw new InvalidOperationException("--role must be 'customer' or 'operator'.");
}

var credentials = new SigningCredentials(
    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
    SecurityAlgorithms.HmacSha256);
var token = new JwtSecurityToken(
    issuer,
    audience,
    claims,
    expires: DateTime.UtcNow.AddMinutes(lifetimeMinutes),
    signingCredentials: credentials);

Console.WriteLine(new JwtSecurityTokenHandler().WriteToken(token));
return 0;

static string GetRequiredOption(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            var value = args[index + 1];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{name} requires a value.");
            }

            return value;
        }
    }

    throw new InvalidOperationException($"Missing required option {name}.");
}

static string? GetOptionalOption(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Local development JWT helper (never use in production).

        Reads signing material from environment:
          JWT_SIGNING_KEY (required, >= 32 chars)
          JWT_ISSUER (default: transfer-orchestration)
          JWT_AUDIENCE (default: transfer-orchestration-api)
          JWT_LIFETIME_MINUTES (default: 15)

        Usage:
          dotnet run --project scripts/LocalDevToken -- --role customer --account-id <guid> [--sub local-customer]
          dotnet run --project scripts/LocalDevToken -- --role operator [--sub local-operator]

        Prints a short-lived bearer token to stdout. Never commit tokens or signing keys.
        """);
}
