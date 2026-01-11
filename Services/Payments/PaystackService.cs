using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LawAfrica.API.Services.Payments
{
    public class PaystackService
    {
        private readonly HttpClient _http;
        private readonly PaystackOptions _opts;

        public PaystackService(HttpClient http, IOptions<PaystackOptions> opts)
        {
            _http = http;
            _opts = opts.Value;
        }

        public record InitializeResult(string AuthorizationUrl, string Reference);

        public record VerifyResult(
            bool IsSuccessful,
            string Status,
            string Reference,
            string Currency,
            decimal AmountMajor,
            string? Channel,
            string? ProviderTransactionId,
            DateTime? PaidAt,
            string RawJson
        );

        /// <summary>
        /// Initialize a Paystack transaction.
        /// Paystack amount must be in the lowest currency unit (e.g., kobo/cents).
        /// </summary>
        public async Task<InitializeResult> InitializeTransactionAsync(
            string email,
            decimal amountMajor,
            string currency,
            string reference,
            string? callbackUrl,
            CancellationToken ct = default)
        {
            EnsureConfigured();

            if (string.IsNullOrWhiteSpace(email))
                throw new InvalidOperationException("Email is required for Paystack initialization.");

            var amountSubunit = ToSubunit(amountMajor);

            var body = new
            {
                email = email.Trim(),
                amount = amountSubunit,
                currency = string.IsNullOrWhiteSpace(currency) ? "KES" : currency.Trim().ToUpperInvariant(),
                reference = reference.Trim(),
                callback_url = string.IsNullOrWhiteSpace(callbackUrl) ? null : callbackUrl.Trim()
            };

            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.paystack.co/transaction/initialize");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.SecretKey);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var res = await _http.SendAsync(req, ct);
            var raw = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Paystack initialize failed ({(int)res.StatusCode}). {TrimForLog(raw)}");

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var ok = root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                var msg = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "Unknown Paystack error.";
                throw new InvalidOperationException($"Paystack initialize rejected: {msg}");
            }

            var data = root.GetProperty("data");
            var authUrl = data.GetProperty("authorization_url").GetString();
            var returnedRef = data.GetProperty("reference").GetString();

            if (string.IsNullOrWhiteSpace(authUrl) || string.IsNullOrWhiteSpace(returnedRef))
                throw new InvalidOperationException("Paystack initialize response missing authorization_url/reference.");

            return new InitializeResult(authUrl!, returnedRef!);
        }

        /// <summary>
        /// Verify a transaction by reference (server-to-server).
        /// Paystack recommends verify before delivering value.
        /// </summary>
        public async Task<VerifyResult> VerifyTransactionAsync(string reference, CancellationToken ct = default)
        {
            EnsureConfigured();

            if (string.IsNullOrWhiteSpace(reference))
                throw new InvalidOperationException("Reference is required for Paystack verification.");

            var url = $"https://api.paystack.co/transaction/verify/{Uri.EscapeDataString(reference.Trim())}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.SecretKey);

            using var res = await _http.SendAsync(req, ct);
            var raw = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Paystack verify failed ({(int)res.StatusCode}). {TrimForLog(raw)}");

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var ok = root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                var msg = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "Unknown Paystack error.";
                return new VerifyResult(false, "unknown", reference, "KES", 0, null, null, null, raw);
            }

            var data = root.GetProperty("data");

            var status = data.TryGetProperty("status", out var stEl) ? (stEl.GetString() ?? "unknown") : "unknown";
            var currency = data.TryGetProperty("currency", out var curEl) ? (curEl.GetString() ?? "KES") : "KES";
            var channel = data.TryGetProperty("channel", out var chEl) ? chEl.GetString() : null;

            // Paystack amounts are typically in subunit
            long amountSub = data.TryGetProperty("amount", out var amtEl) ? amtEl.GetInt64() : 0;
            var amountMajor = amountSub / 100m;

            string? txId = null;
            if (data.TryGetProperty("id", out var idEl))
                txId = idEl.ToString();

            DateTime? paidAt = null;
            if (data.TryGetProperty("paid_at", out var paidEl) && paidEl.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(paidEl.GetString(), out var dt))
                paidAt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

            var isSuccessful = string.Equals(status, "success", StringComparison.OrdinalIgnoreCase);

            return new VerifyResult(isSuccessful, status, reference, currency, amountMajor, channel, txId, paidAt, raw);
        }

        private void EnsureConfigured()
        {
            if (string.IsNullOrWhiteSpace(_opts.SecretKey))
                throw new InvalidOperationException("Paystack SecretKey is not configured.");
        }

        private static long ToSubunit(decimal amountMajor)
        {
            if (amountMajor <= 0) throw new InvalidOperationException("Amount must be greater than zero.");
            return (long)decimal.Round(amountMajor * 100m, 0, MidpointRounding.AwayFromZero);
        }

        private static string TrimForLog(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            raw = raw.Trim();
            return raw.Length <= 500 ? raw : raw.Substring(0, 500) + "...";
        }
    }
}
