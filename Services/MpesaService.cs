using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LawAfrica.API.Settings;
using Microsoft.Extensions.Options;

namespace LawAfrica.API.Services
{
    /// <summary>
    /// Minimal Mpesa Daraja client: OAuth + STK Push.
    /// Callback handling is done in controller.
    /// </summary>
    public class MpesaService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly MpesaSettings _settings;

        public MpesaService(IHttpClientFactory httpClientFactory, IOptions<MpesaSettings> settings)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            var client = _httpClientFactory.CreateClient();

            var auth = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ConsumerKey}:{_settings.ConsumerSecret}")
            );

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);

            var url = $"{_settings.BaseUrl}/oauth/v1/generate?grant_type=client_credentials";
            var resp = await client.GetAsync(url);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            return doc.RootElement.GetProperty("access_token").GetString()!;
        }

        public static string BuildStkPassword(string shortCode, string passKey, string timestamp)
        {
            // password = Base64(ShortCode + PassKey + Timestamp)
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(shortCode + passKey + timestamp));
        }

        public async Task<(string merchantRequestId, string checkoutRequestId, string rawResponse)> InitiateStkPushAsync(
            string accessToken,
            string phoneNumber,
            decimal amount,
            string accountReference,
            string transactionDesc)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var password = BuildStkPassword(_settings.ShortCode, _settings.PassKey, timestamp);

            var payload = new
            {
                BusinessShortCode = _settings.ShortCode,
                Password = password,
                Timestamp = timestamp,
                TransactionType = "CustomerPayBillOnline",
                Amount = (int)Math.Ceiling(amount),
                PartyA = phoneNumber,
                PartyB = _settings.ShortCode,
                PhoneNumber = phoneNumber,
                CallBackURL = _settings.CallbackUrl,
                AccountReference = accountReference,
                TransactionDesc = transactionDesc
            };

            var url = $"{_settings.BaseUrl}/mpesa/stkpush/v1/processrequest";
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            /*var resp = await client.PostAsync(url, content);
            var raw = await resp.Content.ReadAsStringAsync();
            resp.EnsureSuccessStatusCode();*/
            var resp = await client.PostAsync(url, content);
            var raw = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                // IMPORTANT: show the exact Safaricom error
                throw new InvalidOperationException(
                    $"Mpesa STK Push failed ({(int)resp.StatusCode}): {raw}"
                );
            }


            using var doc = JsonDocument.Parse(raw);
            var merchantRequestId = doc.RootElement.GetProperty("MerchantRequestID").GetString()!;
            var checkoutRequestId = doc.RootElement.GetProperty("CheckoutRequestID").GetString()!;

            return (merchantRequestId, checkoutRequestId, raw);
        }
    }
}
