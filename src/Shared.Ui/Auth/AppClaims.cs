namespace Template.Shared.Ui.Auth;

public static class AppClaims
{
    public const string TenantName = "tenant_name";
    public const string Locale = "locale";

    // Server-issued; read API-side to scope tenant queries. Listed here so the claim
    // names stay in one inventory (see Template.Api.Services.JwtTokenService).
    public const string TenantId = "tenant_id";
}
