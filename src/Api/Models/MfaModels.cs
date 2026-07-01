using System.Text.Json.Serialization;

namespace Template.Api.Models;

public record MfaStatusResponse
{
    [JsonPropertyName("enabled")] public required bool Enabled { get; init; }
}

public record MfaEnrollResponse
{
    /// <summary>The <c>otpauth://</c> URI to render as a QR (also carries the secret for manual entry).</summary>
    [JsonPropertyName("provisioning_uri")] public required string ProvisioningUri { get; init; }

    /// <summary>The Base32 secret, for manual entry into an authenticator app.</summary>
    [JsonPropertyName("secret")] public required string Secret { get; init; }
}

public record MfaCodeRequest
{
    [JsonPropertyName("code")] public string? Code { get; init; }
}

public record MfaRecoveryCodesResponse
{
    /// <summary>One-time recovery codes — shown once. Store them somewhere safe.</summary>
    [JsonPropertyName("recovery_codes")] public required IReadOnlyList<string> RecoveryCodes { get; init; }
}
