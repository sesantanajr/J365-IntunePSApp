using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// The certificate this computer uses to connect to Microsoft Graph as the application (app-only, no secret).
    /// Its RSA key is created in the TPM when the computer has one (Microsoft Platform Crypto Provider) and otherwise in
    /// the key store of Windows, never exportable, for the current Windows user only. Entra ID receives only the public
    /// part; every token request is a short assertion signed with the key (RFC 7523), so there is no password to steal.
    /// </summary>
    public static class AppCertificate
    {
        public const string CommonName = "J365 IntunePSApp";

        /// <summary>The validities offered when a certificate is created, in months (at least six).</summary>
        public static readonly int[] ValidityMonths = { 6, 12, 24 };

        public const int DefaultMonths = 12;

        /// <summary>A validity among those offered; anything else is the default.</summary>
        public static int Months(int months) => Array.IndexOf(ValidityMonths, months) >= 0 ? months : DefaultMonths;

        /// <summary>Tries the TPM first. Tests turn it off so they do not leave keys in the TPM of the computer.</summary>
        public static bool UseTpm { get; set; } = true;

        /// <summary>What Settings shows about the certificate in use.</summary>
        public sealed class Details
        {
            public string Thumbprint { get; init; } = "";
            public DateTime NotAfter { get; init; }
            public bool Hardware { get; init; }
            public bool Expired => NotAfter <= DateTime.Now;
            public bool ExpiresSoon => NotAfter <= DateTime.Now.AddDays(30);
        }

        /// <summary>
        /// Creates the certificate and its key and stores it for the current Windows user, valid for
        /// <paramref name="months"/> (6, 12 or 24). The TPM is tried first; <paramref name="softwareOnly"/> skips it (tests,
        /// and computers whose TPM refuses new keys).
        /// </summary>
        public static X509Certificate2 Create(string label, bool softwareOnly = false, int months = DefaultMonths)
        {
            string keyName = "J365-IntunePSApp-" + Guid.NewGuid().ToString("N");
            var providers = softwareOnly || !UseTpm
                ? new[] { CngProvider.MicrosoftSoftwareKeyStorageProvider }
                : new[] { CngProvider.MicrosoftPlatformCryptoProvider, CngProvider.MicrosoftSoftwareKeyStorageProvider };
            CngKey key = null;
            Exception last = null;
            foreach (var provider in providers)
            {
                try
                {
                    var p = new CngKeyCreationParameters { Provider = provider, KeyCreationOptions = CngKeyCreationOptions.None };
                    // A TPM key never leaves the TPM; the key store is told explicitly that this key cannot be exported.
                    if (provider != CngProvider.MicrosoftPlatformCryptoProvider)
                    {
                        p.ExportPolicy = CngExportPolicies.None;
                        p.KeyUsage = CngKeyUsages.Signing;
                    }
                    p.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
                    key = CngKey.Create(CngAlgorithm.Rsa, keyName, p);
                    break;
                }
                catch (CryptographicException ex) { last = ex; }
            }
            if (key == null) throw new CryptographicException(T("Cert_ErrCreate", last?.Message ?? "?"), last);
            try
            {
                using var rsa = new RSACng(key);
                var name = new X500DistinguishedNameBuilder();
                name.AddCommonName(CommonName);
                name.AddOrganizationalUnitName(Environment.MachineName);
                if (!string.IsNullOrWhiteSpace(label)) name.AddOrganizationName(label.Trim());
                var request = new CertificateRequest(name.Build(), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, false));
                request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
                var now = DateTimeOffset.UtcNow;
                using (var created = request.CreateSelfSigned(now.AddMinutes(-10), now.AddMonths(Months(months))))
                {
                    // The certificate refers to the persisted key by its name, so the store keeps the link (the key stays
                    // where it was created, in the TPM or in the key store).
                    using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                    store.Open(OpenFlags.ReadWrite);
                    store.Add(created);
                    var stored = Find(created.Thumbprint) ?? throw new CryptographicException(T("Cert_ErrStore"));
                    // Proves the key signs before anything is sent to Entra ID.
                    using var check = stored.GetRSAPrivateKey() ?? throw new CryptographicException(T("Cert_ErrNoKey"));
                    check.SignData(new byte[] { 1, 2, 3 }, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    return stored;
                }
            }
            catch
            {
                try { key.Delete(); } catch { }
                throw;
            }
            finally { key.Dispose(); }
        }

        /// <summary>The certificate with its private key from the store of the current user, or null.</summary>
        public static X509Certificate2 Find(string thumbprint)
        {
            thumbprint = Normalize(thumbprint);
            if (thumbprint.Length == 0) return null;
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            X509Certificate2 match = null;
            foreach (var c in store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false))
            {
                if (match == null && c.HasPrivateKey) match = c;
                else c.Dispose();
            }
            return match;
        }

        public static bool Exists(string thumbprint)
        {
            using var c = Find(thumbprint);
            return c != null;
        }

        /// <summary>Present, not expired, and its key opens (HasPrivateKey only says that the certificate names a key: a cleared TPM loses it).</summary>
        public static bool IsUsable(string thumbprint)
        {
            using var c = Find(thumbprint);
            if (c == null || c.NotAfter <= DateTime.Now) return false;
            try
            {
                using var key = c.GetRSAPrivateKey();
                return key != null;
            }
            catch (CryptographicException) { return false; }
        }

        /// <summary>Thumbprint, expiry and where the key lives (TPM or key store), or null when the certificate is gone.</summary>
        public static Details Describe(string thumbprint)
        {
            using var c = Find(thumbprint);
            if (c == null) return null;
            bool hardware = false;
            try
            {
                using var rsa = c.GetRSAPrivateKey();
                hardware = rsa is RSACng cng && cng.Key.Provider == CngProvider.MicrosoftPlatformCryptoProvider;
            }
            catch (CryptographicException) { }
            return new Details { Thumbprint = c.Thumbprint, NotAfter = c.NotAfter, Hardware = hardware };
        }

        /// <summary>
        /// Removes a certificate made by this application, and its key. Certificates of anything else are never touched,
        /// whatever the settings say.
        /// </summary>
        public static void Delete(string thumbprint)
        {
            thumbprint = Normalize(thumbprint);
            if (thumbprint.Length == 0) return;
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
            foreach (var c in store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false))
            {
                using (c)
                {
                    if (c.GetNameInfo(X509NameType.SimpleName, false) != CommonName) continue;
                    CngKey key = null;
                    try { key = (c.GetRSAPrivateKey() as RSACng)?.Key; } catch (CryptographicException) { }
                    store.Remove(c);
                    try { key?.Delete(); } catch (CryptographicException) { }
                }
            }
        }

        /// <summary>The public certificate, as Entra ID stores it in keyCredentials (base64 of the DER bytes).</summary>
        public static string PublicKeyBase64(X509Certificate2 certificate) => Convert.ToBase64String(certificate.RawData);

        /// <summary>
        /// The client assertion of a token request: a JWT for <paramref name="audience"/> (the token endpoint), issued by
        /// and for the application, valid for 10 minutes and signed with the key of the certificate. PS256 with the
        /// SHA-256 thumbprint, as Microsoft asks; a TPM that cannot sign with PSS falls back to RS256.
        /// </summary>
        public static string ClientAssertion(X509Certificate2 certificate, string audience, string clientId)
        {
            using var rsa = certificate.GetRSAPrivateKey() ?? throw new CryptographicException(T("Cert_ErrNoKey"));
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["aud"] = audience, ["iss"] = clientId, ["sub"] = clientId, ["jti"] = Guid.NewGuid().ToString(),
                ["nbf"] = now - 60, ["iat"] = now, ["exp"] = now + 600
            }));
            string x5tS256 = Base64Url(SHA256.HashData(certificate.RawData));
            try
            {
                string header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["alg"] = "PS256", ["typ"] = "JWT", ["x5t#S256"] = x5tS256 }));
                string unsigned = header + "." + payload;
                byte[] data = Encoding.ASCII.GetBytes(unsigned);
                byte[] signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                // Entra ID checks PS256 with a salt as long as the hash: a key that signs PSS another way (some TPMs) gets RS256.
                using var check = certificate.GetRSAPublicKey();
                if (check != null && check.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) return unsigned + "." + Base64Url(signature);
            }
            catch (CryptographicException) { }
            {
                string x5t = Base64Url(certificate.GetCertHash());
                string header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["alg"] = "RS256", ["typ"] = "JWT", ["x5t"] = x5t, ["x5t#S256"] = x5tS256 }));
                string unsigned = header + "." + payload;
                return unsigned + "." + Base64Url(rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            }
        }

        public static string Normalize(string thumbprint)
            => new string((thumbprint ?? "").Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant() is { Length: 40 } t ? t : "";

        private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
