using LawAfrica.API.Services.Emails;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/email-templates")]
    [Authorize(Roles = "Admin")]
    public class AdminEmailTemplatesController : ControllerBase
    {
        private readonly IEmailTemplateStore _store;
        private readonly IEmailTemplateRenderer _renderer;
        private readonly IWebHostEnvironment _env;

        public AdminEmailTemplatesController(
            IEmailTemplateStore store,
            IEmailTemplateRenderer renderer,
            IWebHostEnvironment env)
        {
            _store = store;
            _renderer = renderer;
            _env = env;
        }

        // GET /api/admin/email-templates
        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            DevOnly();
            var templates = await _store.ListAsync(ct);
            return Ok(templates);
        }

        // GET /api/admin/email-templates/render-sample/{name}
        [HttpGet("render-sample/{name}")]
        public async Task<IActionResult> RenderSample(string name, CancellationToken ct)
        {
            DevOnly();

            object model = name.ToLowerInvariant() switch
            {
                "email-verification" => new
                {
                    ProductName = "LawAfrica",
                    Year = DateTime.UtcNow.Year.ToString(),
                    SupportEmail = "support@lawafrica.com",
                    DisplayName = "Amina Wanjiku",
                    VerificationUrl = "https://example.com/api/auth/verify-email?token=sample_token"
                },

                "twofactor-setup" => new
                {
                    ProductName = "LawAfrica",
                    Year = DateTime.UtcNow.Year.ToString(),
                    SupportEmail = "support@lawafrica.com",
                    DisplayName = "Amina Wanjiku",
                    SecretKey = "JBSWY3DPEHPK3PXP",
                    SetupToken = "sample_setup_token_123",
                    SetupTokenExpiryUtc = DateTime.UtcNow.AddMinutes(30).ToString("yyyy-MM-dd HH:mm") + " UTC",
                    QrCid = "qrcode"
                },

                _ => new
                {
                    ProductName = "LawAfrica",
                    Year = DateTime.UtcNow.Year.ToString(),
                    SupportEmail = "support@lawafrica.com",
                    DisplayName = "Amina Wanjiku"
                }
            };

            var rendered = await _renderer.RenderAsync(
                templateName: name,
                subject: $"[Sample Preview] {name}",
                model: model,
                ct: ct
            );

            return Ok(new
            {
                rendered.Subject,
                rendered.Html
            });
        }

        // POST /api/admin/email-templates/render/{name}
        // Body: { "tokens": { "DisplayName": "...", "VerificationUrl": "..." } }
        public class RenderTemplateRequest
        {
            public Dictionary<string, string> Tokens { get; set; } = new();
        }

        [HttpPost("render/{name}")]
        public async Task<IActionResult> Render(string name, [FromBody] RenderTemplateRequest req, CancellationToken ct)
        {
            DevOnly();

            req ??= new RenderTemplateRequest();
            var tokens = req.Tokens ?? new Dictionary<string, string>();

            // Wrap tokens into a model with known properties (reflection-friendly)
            var model = new TokenModel(tokens);

            var rendered = await _renderer.RenderAsync(
                templateName: name,
                subject: $"[Preview] {name}",
                model: model,
                ct: ct
            );

            return Ok(new
            {
                rendered.Subject,
                rendered.Html
            });
        }

        private void DevOnly()
        {
            if (!_env.IsDevelopment())
                throw new InvalidOperationException("Email template preview is only enabled in Development.");
        }

        // Reflection-friendly token model
        private sealed class TokenModel
        {
            private readonly Dictionary<string, string> _t;
            public TokenModel(Dictionary<string, string> tokens) => _t = tokens;

            private string Get(string k, string fallback = "") => _t.TryGetValue(k, out var v) ? (v ?? fallback) : fallback;

            public string ProductName => Get(nameof(ProductName), "LawAfrica");
            public string Year => Get(nameof(Year), DateTime.UtcNow.Year.ToString());
            public string SupportEmail => Get(nameof(SupportEmail), "support@lawafrica.example");
            public string DisplayName => Get(nameof(DisplayName), "there");

            public string VerificationUrl => Get(nameof(VerificationUrl));
            public string SecretKey => Get(nameof(SecretKey));
            public string SetupToken => Get(nameof(SetupToken));
            public string SetupTokenExpiryUtc => Get(nameof(SetupTokenExpiryUtc));
            public string QrCid => Get(nameof(QrCid), "qrcode");
        }
    }
}
