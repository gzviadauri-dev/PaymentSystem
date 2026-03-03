using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Payment.API.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", (LoginRequest req, IConfiguration config) =>
        {
            if (!Guid.TryParse(req.LicenseId, out var licenseGuid))
                return Results.BadRequest(new { error = "licenseId must be a valid GUID." });

            var key = config["Jwt:Key"]!;
            var issuer = config["Jwt:Issuer"]!;
            var audience = config["Jwt:Audience"]!;

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, licenseGuid.ToString()),
                new Claim("licenseId", licenseGuid.ToString()),
                new Claim("accountId", licenseGuid.ToString()),
            };

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: credentials);

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            return Results.Ok(new
            {
                token = tokenString,
                accountId = licenseGuid.ToString(),
                licenseId = licenseGuid.ToString(),
            });
        })
        .WithTags("Auth")
        .AllowAnonymous();
    }
}

public record LoginRequest(string LicenseId);
