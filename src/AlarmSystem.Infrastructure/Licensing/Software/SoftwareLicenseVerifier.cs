using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlarmSystem.Application.Abstractions.Licensing;
using AlarmSystem.Infrastructure.Licensing.Cryptography;

namespace AlarmSystem.Infrastructure.Licensing.Software;

public sealed class SoftwareLicenseVerifier(ISoftwareLicenseTrustStore trustStore)
{
    private static readonly HashSet<string> PayloadFields =
    [
        "formatVersion", "productId", "licenseId", "licenseType", "bindingMode",
        "machineIdHash", "issuedAtUtc", "notBeforeUtc", "expiresAtUtc", "features",
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
            var failureCode = exception.FailureKind is
                JwsLicenseFailureKind.Signature or JwsLicenseFailureKind.UntrustedKey
                ? LicenseAuthorizationFailureCodes.SignatureInvalid
                : LicenseAuthorizationFailureCodes.ValidationFailed;
            return Invalid(nowUtc, failureCode, exception.Message, signatureValid: false);
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or CryptographicException or
            DecoderFallbackException or InvalidOperationException or ArgumentException)
        {
            return Invalid(
                nowUtc,
                LicenseAuthorizationFailureCodes.ValidationFailed,
                "The software license payload is invalid.",
                signatureValid: true);
        }
    }

    private static SoftwareLicenseDocument ParsePayload(JsonElement payload, string keyId)
    {
        var typeText = StrictLicenseJson.ReadRequiredString(payload, "licenseType", 16);
        var licenseType = typeText switch
        {
            "DEMO" => SoftwareLicenseType.Demo,
            "SUBSCRIPTION" => SoftwareLicenseType.Subscription,
            "PERPETUAL" => SoftwareLicenseType.Perpetual,
            _ => throw new JsonException("licenseType is not supported."),
        };
        var bindingText = StrictLicenseJson.ReadRequiredString(payload, "bindingMode", 16);
        var bindingMode = bindingText switch
        {
            "MACHINE" => SoftwareLicenseBindingMode.Machine,
            "NONE" => SoftwareLicenseBindingMode.None,
            _ => throw new JsonException("bindingMode is not supported."),
        };

        return new SoftwareLicenseDocument(
            payload.GetProperty("formatVersion").GetInt32(),
            StrictLicenseJson.ReadRequiredString(payload, "productId", 64),
            StrictLicenseJson.ReadRequiredString(payload, "licenseId", 128),
            licenseType,
            bindingMode,
            StrictLicenseJson.ReadNullableString(payload, "machineIdHash", 64),
            StrictLicenseJson.ReadUtc(payload, "issuedAtUtc"),
            StrictLicenseJson.ReadUtc(payload, "notBeforeUtc"),
            StrictLicenseJson.ReadNullableUtc(payload, "expiresAtUtc"),
            StrictLicenseJson.ReadFeatures(payload, "features"),
            keyId);
    }

    private static LicenseValidationResult ValidateClaims(
        SoftwareLicenseDocument document,
        string machineId,
        DateTimeOffset nowUtc)
    {
        if (document.FormatVersion != 2 ||
            !string.Equals(document.ProductId, "AlarmSystem", StringComparison.Ordinal) ||
            document.IssuedAtUtc > document.NotBeforeUtc)
        {
            return ClaimsInvalid(document, nowUtc, "The product, version, or issue dates are invalid.");
        }

        var isFinite = document.LicenseType is
            SoftwareLicenseType.Demo or SoftwareLicenseType.Subscription;
        if ((isFinite &&
             (document.ExpiresAtUtc is null ||
              document.ExpiresAtUtc <= document.NotBeforeUtc)) ||
            (document.LicenseType == SoftwareLicenseType.Perpetual &&
             document.ExpiresAtUtc is not null))
        {
            return ClaimsInvalid(document, nowUtc, "The license expiration policy is invalid.");
        }

        if (document.BindingMode == SoftwareLicenseBindingMode.Machine)
        {
            if (!IsSha256Hex(document.MachineIdHash))
            {
                return ClaimsInvalid(document, nowUtc, "A valid machine ID hash is required.");
            }
        }
        else if (document.MachineIdHash is not null)
        {
            return ClaimsInvalid(document, nowUtc, "A non-bound license must use a null machine ID.");
        }

        if (document.LicenseType is SoftwareLicenseType.Subscription or SoftwareLicenseType.Perpetual &&
            document.BindingMode != SoftwareLicenseBindingMode.Machine)
        {
            return ClaimsInvalid(document, nowUtc, "This license type requires machine binding.");
        }

        bool? machineMatches = null;
        if (document.BindingMode == SoftwareLicenseBindingMode.Machine)
        {
            machineMatches = CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(document.MachineIdHash!.ToUpperInvariant()),
                Encoding.ASCII.GetBytes(machineId.ToUpperInvariant()));
            if (machineMatches != true)
            {
                var status = CreateStatus(
                    document,
                    nowUtc,
                    LicenseState.Invalid,
                    false,
                    LicenseAuthorizationFailureCodes.MachineMismatch,
                    "The software license was issued for a different machine.");
                return LicenseValidationResult.Invalid(
                    status,
                    LicenseAuthorizationFailureCodes.MachineMismatch,
                    status.DiagnosticMessage);
            }
        }

        if (nowUtc < document.NotBeforeUtc)
        {
            var status = CreateStatus(
                document,
                nowUtc,
                LicenseState.Pending,
                machineMatches,
                LicenseAuthorizationFailureCodes.NotYetActive,
                "The software license is valid but is not active yet.");
            return LicenseValidationResult.Valid(status);
        }

        if (document.ExpiresAtUtc is not null && nowUtc >= document.ExpiresAtUtc)
        {
            var status = CreateStatus(
                document,
                nowUtc,
                LicenseState.Expired,
                machineMatches,
                LicenseAuthorizationFailureCodes.Expired,
                "The software license has expired.");
            return LicenseValidationResult.Invalid(
                status,
                LicenseAuthorizationFailureCodes.Expired,
                status.DiagnosticMessage);
        }

        return LicenseValidationResult.Valid(CreateStatus(
            document,
            nowUtc,
            LicenseState.Active,
            machineMatches,
            null,
            "The software license is active."));
    }

    private static LicenseValidationResult ClaimsInvalid(
        SoftwareLicenseDocument document,
        DateTimeOffset nowUtc,
        string diagnostic)
    {
        var status = CreateStatus(
            document,
            nowUtc,
            LicenseState.Invalid,
            null,
            LicenseAuthorizationFailureCodes.ValidationFailed,
            diagnostic);
        return LicenseValidationResult.Invalid(
            status,
            LicenseAuthorizationFailureCodes.ValidationFailed,
            diagnostic);
    }

    private static LicenseStatus CreateStatus(
        SoftwareLicenseDocument document,
        DateTimeOffset nowUtc,
        LicenseState state,
        bool? machineMatches,
        string? failureCode,
        string diagnostic) =>
        new(
            LicenseSource.Software,
            state,
            document.LicenseId,
            document.LicenseType.ToString().ToUpperInvariant(),
            document.IssuedAtUtc,
            document.NotBeforeUtc,
            document.ExpiresAtUtc,
            document.ExpiresAtUtc is null
                ? null
                : Math.Max(0, (int)Math.Ceiling((document.ExpiresAtUtc.Value - nowUtc).TotalDays)),
            machineMatches,
            document.Features,
            nowUtc,
            failureCode,
            diagnostic,
            FormatVersion: 2,
            BindingMode: document.BindingMode.ToString().ToUpperInvariant(),
            SignatureValid: true);

    private static LicenseValidationResult Invalid(
        DateTimeOffset nowUtc,
        string failureCode,
        string diagnostic,
        bool signatureValid)
    {
        var status = new LicenseStatus(
            LicenseSource.Software,
            LicenseState.Invalid,
            null,
            "Software V2",
            null,
            null,
            null,
            null,
            null,
            [],
            nowUtc,
            failureCode,
            diagnostic,
            FormatVersion: 2,
            SignatureValid: signatureValid);
        return LicenseValidationResult.Invalid(status, failureCode, diagnostic);
    }

    private static bool IsSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
