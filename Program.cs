using LawAfrica.API;
using LawAfrica.API.Authorization.Handlers;
using LawAfrica.API.Authorization.Policies;
using LawAfrica.API.Authorization.Requirements;
using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Services;
using LawAfrica.API.Services.Ai;
using LawAfrica.API.Services.Documents;
using LawAfrica.API.Services.Emails;
using LawAfrica.API.Services.Institutions;
using LawAfrica.API.Services.LawReportsContent;
using LawAfrica.API.Services.Payments;
using LawAfrica.API.Services.Subscriptions;
using LawAfrica.API.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenAI.Chat;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------
// Configuration
// --------------------------------------------------
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

// --------------------------------------------------
// Database
// --------------------------------------------------
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// --------------------------------------------------
// Controllers
// --------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// --------------------------------------------------
// Options Binding
// --------------------------------------------------
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

var emailSettings = builder.Configuration.GetSection("EmailSettings").Get<EmailSettings>()
    ?? throw new InvalidOperationException("EmailSettings section is missing.");

var hasGraph =
    !string.IsNullOrWhiteSpace(emailSettings.GraphTenantId) &&
    !string.IsNullOrWhiteSpace(emailSettings.GraphClientId) &&
    !string.IsNullOrWhiteSpace(emailSettings.GraphClientSecret);

if (!hasGraph && string.IsNullOrWhiteSpace(emailSettings.Host))
    throw new InvalidOperationException("EmailSettings:Host is missing (SMTP mode).");

// --------------------------------------------------
// Core Services
// --------------------------------------------------
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserApprovalService>();
builder.Services.AddScoped<RegistrationService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<InstitutionService>();
builder.Services.AddHostedService<LawAfrica.API.Services.InstitutionSubscriptionStatusHostedService>();
builder.Services.AddScoped<LawAfrica.API.Services.InstitutionAccessService>();

// --------------------------------------------------
// Payment & Subscription Services
// --------------------------------------------------
builder.Services.Configure<MpesaSettings>(builder.Configuration.GetSection("Mpesa"));
builder.Services.AddHttpClient();
builder.Services.AddScoped<MpesaService>();
builder.Services.AddScoped<PurchaseService>();
builder.Services.AddScoped<InstitutionSubscriptionService>();
builder.Services.AddScoped<PaymentFinalizerService>();
builder.Services.AddScoped<PaymentQueryService>();
builder.Services.AddScoped<PaymentValidationService>();
builder.Services.AddScoped<LawAfrica.API.Services.Payments.LegalDocumentPurchaseFulfillmentService>();
builder.Services.AddScoped<SubscriptionAccessGuard>();
builder.Services.Configure<LawAfrica.API.Models.Payments.PaymentHealingOptions>(
    builder.Configuration.GetSection("PaymentHealing"));
builder.Services.AddScoped<PaymentHealingService>();
builder.Services.Configure<LawAfrica.API.Models.Payments.PaymentHealingSchedulerOptions>(
    builder.Configuration.GetSection("PaymentHealingScheduler"));
builder.Services.AddHostedService<LawAfrica.API.Services.Payments.PaymentHealingHostedService>();
builder.Services.AddScoped<LawAfrica.API.Services.Payments.AdminPaymentsKpiService>();

builder.Services.AddScoped<InstitutionAccessCodeGenerator>();
builder.Services.AddScoped<InstitutionRegistrationNumberGenerator>();
builder.Services.AddScoped<InvoiceNumberGenerator>();

// --------------------------------------------------
// Institution & Document Services
// --------------------------------------------------
builder.Services.AddScoped<InstitutionSeatService>();
builder.Services.AddScoped<DocumentEntitlementService>();
builder.Services.AddScoped<LegalDocumentTocService>();
builder.Services.AddScoped<FileStorageService>();
builder.Services.AddScoped<ReadingProgressService>();
builder.Services.AddScoped<IDocumentIndexingService, PdfDocumentIndexingService>();
builder.Services.AddScoped<InstitutionSeatGuard>();

builder.Services.AddHttpContextAccessor();

// --------------------------------------------------
// Authorization
// --------------------------------------------------
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", p => p.RequireRole("Admin"));
    options.AddPolicy("RequireUser", p => p.RequireRole("User", "Admin"));
    options.AddPolicy(PolicyNames.ApprovedUserOnly,
        p => p.Requirements.Add(new ApprovedUserRequirement()));
});

builder.Services.AddScoped<IAuthorizationHandler, ApprovedUserHandler>();
builder.Services.AddScoped<IAuthorizationHandler, InstitutionAdminHandler>();
builder.Services.AddScoped<IAuthorizationHandler, GlobalAdminHandler>();

// --------------------------------------------------
// OpenAI / AI
// --------------------------------------------------
builder.Services.AddSingleton<ChatClient>(_ =>
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY missing.");
    return new ChatClient("gpt-4o-mini", apiKey);
});

builder.Services.AddScoped<ILawReportSummarizer, OpenAiLawReportSummarizer>();

// --------------------------------------------------
// JWT Authentication
// --------------------------------------------------
var jwtSection = builder.Configuration.GetSection("Jwt");
var secretKey = Encoding.UTF8.GetBytes(jwtSection["Key"]!);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(secretKey)
        };
    });

// --------------------------------------------------
// 🔧 CHANGED #1 — CORS: DEFAULT POLICY ONLY
// --------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "https://lawafricadigitalhub.pages.dev"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// --------------------------------------------------
// Swagger
// --------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// --------------------------------------------------
// 🔧 CHANGED #2 — ROUTING FIRST
// --------------------------------------------------
app.UseRouting();

// --------------------------------------------------
// 🔧 CHANGED #3 — CORS IMMEDIATELY AFTER ROUTING
// (Ensures headers exist even for OPTIONS / 401 / 403)
// --------------------------------------------------
app.UseCors();

// --------------------------------------------------
// 🔧 CHANGED #4 — GLOBAL OPTIONS HANDLER (NO RequireCors)
// --------------------------------------------------
app.MapMethods("{*path}", new[] { "OPTIONS" }, () => Results.Ok());

// --------------------------------------------------
// Exception handling (CORS already applied)
// --------------------------------------------------
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (ex != null)
            app.Logger.LogError(ex, "Unhandled exception");

        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\":\"Internal server error\"}");
    });
});

// --------------------------------------------------
// Static files
// --------------------------------------------------
app.UseStaticFiles();

// --------------------------------------------------
// Custom middleware
// --------------------------------------------------
app.UseMiddleware<ApiExceptionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

// --------------------------------------------------
// 🔧 CHANGED #5 — Controllers WITHOUT RequireCors
// --------------------------------------------------
app.MapControllers();

// --------------------------------------------------
// Health
// --------------------------------------------------
app.MapGet("/", () => Results.Ok(new { status = "ok", service = "LawAfrica.API" }));
app.MapGet("/health", () => Results.Ok("ok"));

app.Run();
