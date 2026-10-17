using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Infrastructure.Licensing.Cryptography;

namespace AlarmSystem.Infrastructure.Licensing.Demo;

public sealed class DemoLicenseVerifier(IDemoLicenseTrustStore trustStore)
{
    private static readonly HashSet<string> PayloadFields =
    [
        "formatVersion", "productId", "licenseId", "licenseType", "machineIdHash",
        "issuedAtUtc", "notBeforeUtc", "expiresAtUtc", "features",
    ];

    public LicenseValidationResult Verify(
        ReadOnlyMemory<byte> licenseContent,
        string machineId,
        DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);
        nowUtc = nowUtc.ToUniversalTime();

        try
        {
            var verified = JwsCompactLicenseVerifier.Verify(licenseContent, trustStore);
            using var payload = StrictLicenseJson.ParseObject(verified.Payload, PayloadFields);
            var document = ParsePayload(payload.RootElement, verified.KeyId);
            return ValidateClaims(document, machineId, nowUtc);
        }
        catch (JwsLicenseException exception)
        {
            return Invalid(nowUtc, exception.Message, signatureValid: false);
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or CryptographicException or
            DecoderFallbackException or InvalidOperationException or ArgumentException)
        {
            return Invalid(nowUtc, "The license payload is invalid.", signatureValid: true);
        }
    }

    private static DemoLicenseDocument ParsePayload(JsonElement payload, string keyId)
    {
        var formatVersion = payload.GetProperty("formatVersion").GetInt32();
        return new DemoLicenseDocument(
            formatVersion,
            StrictLicenseJson.ReadRequiredString(payload, "productId", 64),
            StrictLicenseJson.ReadRequiredString(payload, "licenseId", 128),
            StrictLicenseJson.ReadRequiredString(payload, "licenseType", 16),
            StrictLicenseJson.ReadRequiredString(payload, "machineIdHash", 128),
            StrictLicenseJson.ReadUtc(payload, "issuedAtUtc"),
            StrictLicenseJson.ReadUtc(payload, "notBeforeUtc"),
            StrictLicenseJson.ReadUtc(payload, "expiresAtUtc"),
            StrictLicenseJson.ReadFeatures(payload, "features"),
            keyId);
    }

    private static LicenseValidationResult ValidateClaims(
        DemoLicenseDocument document,
        string machineId,
        DateTimeOffset nowUtc)
    {
        var machineMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(document.MachineIdHash.ToUpperInvariant()),
            Encoding.ASCII.GetBytes(machineId.ToUpperInvariant()));
        var state = LicenseState.Invalid;
        var failureCode = LicenseAuthorizationFailureCodes.ValidationFailed;
        var diagnostic = "The license claims are invalid.";

        if (document.FormatVersion != 1 ||
            !string.Equals(document.ProductId, "AlarmSystem", StringComparison.Ordinal) ||
            !string.Equals(document.LicenseType, "DEMO", StringComparison.Ordinal) ||
            document.IssuedAtUtc > document.NotBeforeUtc ||
            (document.ExpiresAtUtc - document.NotBeforeUtc != TimeSpan.FromDays(15) &&
             document.ExpiresAtUtc - document.NotBeforeUtc != TimeSpan.FromDays(30)))
        {
            diagnostic = "The license product, type, version, or duration is invalid.";
        }
        else if (!machineMatches)
        {
            diagnostic = "The license was issued for a different machine.";
        }
        else if (nowUtc < document.NotBeforeUtc)
        {
            diagnostic = "The license is not active yet.";
        }
        else if (nowUtc >= document.ExpiresAtUtc)
        {
            state = LicenseState.Expired;
            failureCode = LicenseAuthorizationFailureCodes.Expired;
            diagnostic = "The demo license has expired.";
        }
        else
        {
            var status = CreateStatus(document, nowUtc, LicenseState.Active, true, null, "Demo license is active.");
            return LicenseValidationResult.Valid(status);
        }

        var invalidStatus = CreateStatus(
            document,
            nowUtc,
            state,
            machineMatches,
            failureCode,
            diagnostic);
        return LicenseValidationResult.Invalid(invalidStatus, failureCode, diagnostic);
    }

    private static LicenseStatus CreateStatus(
        DemoLicenseDocument document,
        DateTimeOffset nowUtc,
        LicenseState state,
        bool machineMatches,
        string? failureCode,
        string diagnostic) =>
        new(
            LicenseSource.Demo,
            state,
            document.LicenseId,
            "Demo",
            document.IssuedAtUtc,
            document.NotBeforeUtc,
            document.ExpiresAtUtc,
            Math.Max(0, (int)Math.Ceiling((document.ExpiresAtUtc - nowUtc).TotalDays)),
            machineMatches,
            document.Features,
            nowUtc,
            failureCode,
            diagnostic,
            FormatVersion: 1,
            BindingMode: "MACHINE",
            SignatureValid: true);

    private static LicenseValidationResult Invalid(
        DateTimeOffset nowUtc,
        string diagnostic,
        bool signatureValid)
    {
        var status = new LicenseStatus(
            LicenseSource.Demo,
            LicenseState.Invalid,
            null,
            "Demo",
            null,
            null,
            null,
            null,
            null,
            [],
            nowUtc,
            LicenseAuthorizationFailureCodes.ValidationFailed,
            diagnostic,
            FormatVersion: 1,
            BindingMode: "MACHINE",
            SignatureValid: signatureValid);
        return LicenseValidationResult.Invalid(
            status,
            LicenseAuthorizationFailureCodes.ValidationFailed,
            diagnostic);
    }

}
