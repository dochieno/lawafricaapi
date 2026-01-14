// Services/EmailService.cs
// Microsoft Graph (app-only) email sender.
// Drop-in replacement for your SMTP EmailService: same public methods/signatures.
// NOTE: Using statements kept from your original file + only added required ones.

using LawAfrica.API.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LawAfrica.API.Services
{
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

            var fromUser = (_settings.FromEmail ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fromUser))
                throw new InvalidOperationException("EmailSettings:FromEmail is missing. Graph requires a sending mailbox (FromEmail).");

            var accessToken = await GetGraphAccessTokenAsync();

            var payload = new
            {
                message = new
                {
                    subject = subject,
                    body = new
                    {
                        contentType = "HTML",
                        content = htmlMessage
                    },
                    toRecipients = new[]
                    {
                        new { emailAddress = new { address = toEmail } }
                    }
                },
                saveToSentItems = true
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

            var fromUser = (_settings.FromEmail ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fromUser))
                throw new InvalidOperationException("EmailSettings:FromEmail is missing. Graph requires a sending mailbox (FromEmail).");

            var accessToken = await GetGraphAccessTokenAsync();

            // Inline image as attachment with CID
            var attachment = new
            {
                // IMPORTANT: Graph uses "@odata.type"
                // We must write it exactly like this to work.
                @odata = "#microsoft.graph.fileAttachment",
                name = "qrcode.png",
                contentType = "image/png",
                contentBytes = Convert.ToBase64String(imageBytes ?? Array.Empty<byte>()),
                isInline = true,
                contentId = contentId
            };

            var payload = new
            {
                message = new
                {
                    subject = subject,
                    body = new
                    {
                        contentType = "HTML",
                        content = htmlBody
                    },
                    toRecipients = new[]
                    {
                        new { emailAddress = new { address = toEmail } }
                    },
                    attachments = new object[] { attachment }
                },
                saveToSentItems = true
            };

            await SendMailViaGraphAsync(accessToken, fromUser, payload);
        }

        // ---------------------------
        // Graph internals
        // ---------------------------

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

        private async Task SendMailViaGraphAsync(string accessToken, string fromUser, object payload)
        {
            var http = _httpClientFactory.CreateClient();

            var url = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(fromUser)}/sendMail";

            var body = JsonSerializer.Serialize(payload);

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req);
            var respBody = await resp.Content.ReadAsStringAsync();

            // Graph sendMail typically returns 202 Accepted on success
            if ((int)resp.StatusCode < 200 || (int)resp.StatusCode >= 300)
                throw new InvalidOperationException($"Graph sendMail failed: {(int)resp.StatusCode}. {respBody}");
        }
    }
}
