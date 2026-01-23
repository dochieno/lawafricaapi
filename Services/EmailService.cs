// Services/EmailService.cs
// Microsoft Graph (app-only) email sender.
// ✅ Updated to support: SendEmailWithAttachmentsAsync (PDF invoices etc.)
// ✅ Keeps existing public methods/signatures unchanged
// ✅ Adds a small EmailAttachment model (non-breaking)

using LawAfrica.API.Models;
using LawAfrica.API.Models.Emails;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LawAfrica.API.Services
{
    /// <summary>
    /// Microsoft Graph (app-only) email sender.
    /// Supports:
    /// - HTML email
    /// - Inline images via CID (isInline=true)
    /// - Attachments (e.g., PDF invoice)
    /// </summary>
    public class EmailService
    {
        private readonly EmailSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;

        public EmailService(IOptions<EmailSettings> settings, IHttpClientFactory httpClientFactory)
        {
            _settings = settings.Value;
            _httpClientFactory = httpClientFactory;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlMessage)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
                return;

            var fromUser = GetFromUserOrThrow();
            var accessToken = await GetGraphAccessTokenAsync();

            var payload = new Dictionary<string, object?>
            {
                ["message"] = new Dictionary<string, object?>
                {
                    ["subject"] = subject ?? "",
                    ["body"] = new Dictionary<string, object?>
                    {
                        ["contentType"] = "HTML",
                        ["content"] = htmlMessage ?? ""
                    },
                    ["toRecipients"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["emailAddress"] = new Dictionary<string, object?>
                            {
                                ["address"] = toEmail.Trim()
                            }
                        }
                    }
                },
                ["saveToSentItems"] = true
            };

            await SendMailViaGraphAsync(accessToken, fromUser, payload);
        }

        public async Task SendEmailWithInlineImageAsync(
            string toEmail,
            string subject,
            string htmlBody,
            byte[] imageBytes,
            string contentId)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
                return;

            var fromUser = GetFromUserOrThrow();
            var accessToken = await GetGraphAccessTokenAsync();

            // ✅ Graph requires "@odata.type" (exact key) for attachments.
            var attachment = new Dictionary<string, object?>
            {
                ["@odata.type"] = "#microsoft.graph.fileAttachment",
                ["name"] = "qrcode.png",
                ["contentType"] = "image/png",
                ["contentBytes"] = Convert.ToBase64String(imageBytes ?? Array.Empty<byte>()),
                ["isInline"] = true,
                ["contentId"] = (contentId ?? "").Trim()
            };

            var payload = new Dictionary<string, object?>
            {
                ["message"] = new Dictionary<string, object?>
                {
                    ["subject"] = subject ?? "",
                    ["body"] = new Dictionary<string, object?>
                    {
                        ["contentType"] = "HTML",
                        ["content"] = htmlBody ?? ""
                    },
                    ["toRecipients"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["emailAddress"] = new Dictionary<string, object?>
                            {
                                ["address"] = toEmail.Trim()
                            }
                        }
                    },
                    ["attachments"] = new object[] { attachment }
                },
                ["saveToSentItems"] = true
            };

            await SendMailViaGraphAsync(accessToken, fromUser, payload);
        }

        /// <summary>
        /// ✅ NEW: Send HTML email with one or more file attachments (PDF invoices etc.)
        /// </summary>
        public async Task SendEmailWithAttachmentsAsync(
            string toEmail,
            string subject,
            string htmlBody,
            IEnumerable<EmailAttachment> attachments,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
                return;

            var fromUser = GetFromUserOrThrow();
            var accessToken = await GetGraphAccessTokenAsync();

            var attList = new List<object>();

            foreach (var a in attachments ?? Array.Empty<EmailAttachment>())
            {
                if (a == null) continue;

                var bytes = a.Bytes ?? Array.Empty<byte>();
                if (bytes.Length == 0) continue;

                var fileName = string.IsNullOrWhiteSpace(a.FileName) ? "attachment" : a.FileName.Trim();
                var contentType = string.IsNullOrWhiteSpace(a.ContentType) ? "application/octet-stream" : a.ContentType.Trim();

                attList.Add(new Dictionary<string, object?>
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = fileName,
                    ["contentType"] = contentType,
                    ["contentBytes"] = Convert.ToBase64String(bytes),
                    ["isInline"] = false
                });
            }

            var payload = new Dictionary<string, object?>
            {
                ["message"] = new Dictionary<string, object?>
                {
                    ["subject"] = subject ?? "",
                    ["body"] = new Dictionary<string, object?>
                    {
                        ["contentType"] = "HTML",
                        ["content"] = htmlBody ?? ""
                    },
                    ["toRecipients"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["emailAddress"] = new Dictionary<string, object?>
                            {
                                ["address"] = toEmail.Trim()
                            }
                        }
                    },
                    // Only include attachments if any exist (Graph is okay either way, but cleaner)
                    ["attachments"] = attList.Count > 0 ? attList.ToArray() : Array.Empty<object>()
                },
                ["saveToSentItems"] = true
            };

            await SendMailViaGraphAsync(accessToken, fromUser, payload, ct);
        }

        /// <summary>
        /// Optional helper: Send HTML email with attachments + inline images together.
        /// (Not required now, but useful if you ever attach PDF + include inline logo)
        /// </summary>
        public async Task SendEmailWithAttachmentsAndInlineImagesAsync(
            string toEmail,
            string subject,
            string htmlBody,
            IEnumerable<EmailAttachment>? attachments,
            IEnumerable<EmailInlineImage>? inlineImages,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
                return;

            var fromUser = GetFromUserOrThrow();
            var accessToken = await GetGraphAccessTokenAsync();

            var attList = new List<object>();

            // Inline images
            foreach (var img in inlineImages ?? Array.Empty<EmailInlineImage>())
            {
                if (img?.Bytes == null || img.Bytes.Length == 0) continue;

                var cid = string.IsNullOrWhiteSpace(img.ContentId) ? "inline" : img.ContentId.Trim();
                var fileName = string.IsNullOrWhiteSpace(img.FileName) ? $"{cid}.png" : img.FileName!.Trim();
                var contentType = string.IsNullOrWhiteSpace(img.ContentType) ? "image/png" : img.ContentType.Trim();

                attList.Add(new Dictionary<string, object?>
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = fileName,
                    ["contentType"] = contentType,
                    ["contentBytes"] = Convert.ToBase64String(img.Bytes),
                    ["isInline"] = true,
                    ["contentId"] = cid
                });
            }

            // Regular attachments
            foreach (var a in attachments ?? Array.Empty<EmailAttachment>())
            {
                if (a?.Bytes == null || a.Bytes.Length == 0) continue;

                var fileName = string.IsNullOrWhiteSpace(a.FileName) ? "attachment" : a.FileName.Trim();
                var contentType = string.IsNullOrWhiteSpace(a.ContentType) ? "application/octet-stream" : a.ContentType.Trim();

                attList.Add(new Dictionary<string, object?>
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = fileName,
                    ["contentType"] = contentType,
                    ["contentBytes"] = Convert.ToBase64String(a.Bytes),
                    ["isInline"] = false
                });
            }

            var payload = new Dictionary<string, object?>
            {
                ["message"] = new Dictionary<string, object?>
                {
                    ["subject"] = subject ?? "",
                    ["body"] = new Dictionary<string, object?>
                    {
                        ["contentType"] = "HTML",
                        ["content"] = htmlBody ?? ""
                    },
                    ["toRecipients"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["emailAddress"] = new Dictionary<string, object?>
                            {
                                ["address"] = toEmail.Trim()
                            }
                        }
                    },
                    ["attachments"] = attList.Count > 0 ? attList.ToArray() : Array.Empty<object>()
                },
                ["saveToSentItems"] = true
            };

            await SendMailViaGraphAsync(accessToken, fromUser, payload, ct);
        }

        // ---------------------------
        // Graph internals
        // ---------------------------

        private string GetFromUserOrThrow()
        {
            var fromUser = (_settings.FromEmail ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fromUser))
                throw new InvalidOperationException(
                    "EmailSettings:FromEmail is missing. Graph requires a sending mailbox (FromEmail).");
            return fromUser;
        }

        private async Task<string> GetGraphAccessTokenAsync()
        {
            var tenantId = (_settings.GraphTenantId ?? "").Trim();
            var clientId = (_settings.GraphClientId ?? "").Trim();
            var clientSecret = (_settings.GraphClientSecret ?? "").Trim();

            if (string.IsNullOrWhiteSpace(tenantId))
                throw new InvalidOperationException("EmailSettings:GraphTenantId is missing.");
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("EmailSettings:GraphClientId is missing.");
            if (string.IsNullOrWhiteSpace(clientSecret))
                throw new InvalidOperationException("EmailSettings:GraphClientSecret is missing.");

            var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";

            var http = _httpClientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["grant_type"] = "client_credentials",
                    ["scope"] = "https://graph.microsoft.com/.default"
                })
            };

            using var resp = await http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Graph token request failed: {(int)resp.StatusCode}. {json}");

            using var doc = JsonDocument.Parse(json);
            var token = doc.RootElement.GetProperty("access_token").GetString();

            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Graph token response did not include access_token.");

            return token;
        }

        private async Task SendMailViaGraphAsync(string accessToken, string fromUser, object payload, CancellationToken ct = default)
        {
            var http = _httpClientFactory.CreateClient();
            var url = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(fromUser)}/sendMail";

            var body = JsonSerializer.Serialize(payload);

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct);
            var respBody = await resp.Content.ReadAsStringAsync(ct);

            if ((int)resp.StatusCode < 200 || (int)resp.StatusCode >= 300)
                throw new InvalidOperationException($"Graph sendMail failed: {(int)resp.StatusCode}. {respBody}");
        }
    }

    /// <summary>
    /// ✅ NEW: Attachment model for Graph email sending.
    /// Keep it simple: used by Invoice emailing.
    /// </summary>
    public class EmailAttachment
    {
        public string FileName { get; set; } = "attachment";
        public string ContentType { get; set; } = "application/octet-stream";
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
    }
}
