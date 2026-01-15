// Program.cs (FULL FILE)
// ✅ Minimal change: do NOT require EmailSettings.Host when using Microsoft Graph.
// ✅ Adds persistent disk support via STORAGE_ROOT env var on Render.
// ✅ Adds deterministic GET /storage/** endpoint to eliminate 405 for cover images on Render.
// ✅ Ensures Render/Vercel env vars override JSON via AddEnvironmentVariables().
// ✅ Keeps existing logic unchanged.

using LawAfrica.API;
using LawAfrica.API.Authorization.Handlers;
using LawAfrica.API.Authorization.Policies;
using LawAfrica.API.Authorization.Requirements;
using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Services;
using LawAfrica.API.Services.Documents;
using LawAfrica.API.Services.Emails;
using LawAfrica.API.Services.Institutions;
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
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------
// Configuration
// --------------------------------------------------
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    // ✅ IMPORTANT: allow Render env vars to override appsettings.json
    // For nested keys: Mpesa__ConsumerKey, Paystack__SecretKey, etc.
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
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// --------------------------------------------------
// Options Binding
// --------------------------------------------------
builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));

var emailSettings = builder.Configuration.GetSection("EmailSettings").Get<EmailSettings>();
if (emailSettings == null)
    throw new InvalidOperationException("EmailSettings section is missing.");

// ✅ UPDATED VALIDATION (Graph-aware)
var hasGraph =
    !string.IsNullOrWhiteSpace(emailSettings.GraphTenantId) &&
    !string.IsNullOrWhiteSpace(emailSettings.GraphClientId) &&
    !string.IsNullOrWhiteSpace(emailSettings.GraphClientSecret);

// If Graph vars are NOT set, we assume SMTP mode and require Host.
if (!hasGraph && string.IsNullOrWhiteSpace(emailSettings.Host))
    throw new InvalidOperationException("EmailSettings:Host is missing or empty (SMTP mode). For Graph, set GraphTenantId/GraphClientId/GraphClientSecret.");

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
builder.Services.Configure<LawAfrica.API.Models.Payments.PaymentHealingOptions>(builder.Configuration.GetSection("PaymentHealing"));
builder.Services.AddScoped<PaymentHealingService>();
builder.Services.Configure<LawAfrica.API.Models.Payments.PaymentHealingSchedulerOptions>(builder.Configuration.GetSection("PaymentHealingScheduler"));
builder.Services.AddHostedService<LawAfrica.API.Services.Payments.PaymentHealingHostedService>();
builder.Services.AddScoped<LawAfrica.API.Services.Payments.AdminPaymentsKpiService>();

// Numbering
builder.Services.AddScoped<InstitutionAccessCodeGenerator>();
builder.Services.AddScoped<InstitutionRegistrationNumberGenerator>();
builder.Services.AddScoped<InvoiceNumberGenerator>();

// --------------------------------------------------
// Institution & Document Services
// --------------------------------------------------
builder.Services.AddScoped<InstitutionSeatService>();
builder.Services.AddScoped<DocumentEntitlementService>();
builder.Services.AddScoped<FileStorageService>();
builder.Services.AddScoped<ReadingProgressService>();
builder.Services.AddScoped<IDocumentIndexingService, PdfDocumentIndexingService>();
builder.Services.AddScoped<InstitutionSeatGuard>();

// Usage / Admin
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<LawAfrica.API.Services.Usage.IUsageEventWriter, LawAfrica.API.Services.Usage.UsageEventWriter>();
builder.Services.AddScoped<LawAfrica.API.Services.Usage.UsageEventLogger>();

// Paystack
builder.Services.Configure<PaystackOptions>(builder.Configuration.GetSection("Paystack"));
builder.Services.AddHttpClient<PaystackService>();
builder.Services.AddScoped<PaymentReconciliationService>();

// Email templates
builder.Services.AddSingleton<IEmailTemplateStore, FileEmailTemplateStore>();
builder.Services.AddSingleton<IEmailTemplateRenderer, SimpleTokenEmailTemplateRenderer>();
builder.Services.AddScoped<EmailComposer>();

// --------------------------------------------------
// Authorization
// --------------------------------------------------
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireUser", policy => policy.RequireRole("User", "Admin"));

    options.AddPolicy(PolicyNames.ApprovedUserOnly,
        policy => policy.Requirements.Add(new ApprovedUserRequirement()));

    options.AddPolicy(PolicyNames.IsInstitutionAdmin,
        policy => policy.Requirements.Add(new InstitutionAdminRequirement()));

    options.AddPolicy(PolicyNames.CanApproveInstitutionUsers,
        policy => policy.Requirements.Add(new CanApproveInstitutionUsersRequirement()));

    options.AddPolicy(PolicyNames.IsGlobalAdmin,
        policy => policy.Requirements.Add(new GlobalAdminRequirement()));

    options.AddPolicy("Permissions.Users.Create",
        p => p.Requirements.Add(new PermissionRequirement("users.create")));

    options.AddPolicy("Permissions.Users.Approve",
        p => p.Requirements.Add(new PermissionRequirement("users.approve")));

    options.AddPolicy("Permissions.Records.Delete",
        p => p.Requirements.Add(new PermissionRequirement("records.delete")));

    options.AddPolicy("Permissions.Payments.Reconcile",
        p => p.Requirements.Add(new PermissionRequirement("payments.reconcile")));
});

// Handlers
builder.Services.AddScoped<IAuthorizationHandler, ApprovedUserHandler>();
builder.Services.AddScoped<IAuthorizationHandler, InstitutionAdminHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CanApproveInstitutionUsersHandler>();
builder.Services.AddScoped<IAuthorizationHandler, GlobalAdminHandler>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

// --------------------------------------------------
// JWT Authentication
// --------------------------------------------------
var jwtSection = builder.Configuration.GetSection("Jwt");
var keyFromConfig = jwtSection["Key"];

if (string.IsNullOrWhiteSpace(keyFromConfig))
    throw new InvalidOperationException("Jwt:Key is missing.");

var secretKey = Encoding.UTF8.GetBytes(keyFromConfig);

if (secretKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 characters.");

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
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
// ✅ CORS (FIXED)
// --------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("ViteDev", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "https://lawafricadigitalhub.vercel.app",
                "https://www.lawafricadigitalhub.vercel.app"
              )
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// --------------------------------------------------
// Swagger
// --------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LawAfrica API",
        Version = "v1"
    });

    c.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using the Bearer scheme."
    });

    // ✅ FIX: AddSecurityRequirement expects an OpenApiSecurityRequirement object (not a lambda)
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "bearer"
                }
            },
            new List<string>()
        }
    });
});

var app = builder.Build();

// --------------------------------------------------
// ✅ Global exception handler (logs real 500 causes on Render)
// --------------------------------------------------
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var ex = feature?.Error;

        if (ex != null)
        {
            app.Logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
        }

        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\":\"Internal server error\"}");
    });
});

// --------------------------------------------------
// ✅ STORAGE (PERSISTENT DISK READY) + deterministic GET /storage/**
// - Uses STORAGE_ROOT if provided (Render Disk mount recommended: /var/data/Storage)
// - Falls back to ./Storage for local dev
// - Registers BEFORE routing so /storage isn't captured by routing / CORS maps
// --------------------------------------------------
var storageRoot = Environment.GetEnvironmentVariable("STORAGE_ROOT");

if (string.IsNullOrWhiteSpace(storageRoot))
{
    storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "Storage");
}

// Normalize + ensure exists
Directory.CreateDirectory(storageRoot);

// Optional: log path so you can confirm on Render logs
app.Logger.LogInformation("Storage root: {StorageRoot}", storageRoot);

// Default wwwroot (harmless if no wwwroot)
app.UseStaticFiles();

// Static mapping (keep)
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(storageRoot),
    RequestPath = "/storage"
});

// ✅ Hard guarantee: GET /storage/** always serves files (prevents 405 "Method Not Allowed")
var contentTypes = new FileExtensionContentTypeProvider();
app.MapGet("/storage/{**filePath}", (string filePath) =>
{
    if (string.IsNullOrWhiteSpace(filePath))
        return Results.NotFound();

    var clean = filePath.Replace('\\', '/').TrimStart('/');

    // basic traversal protection
    if (clean.Contains(".."))
        return Results.BadRequest("Invalid path.");

    var fullPath = Path.Combine(storageRoot, clean);

    if (!System.IO.File.Exists(fullPath))
        return Results.NotFound();

    if (!contentTypes.TryGetContentType(fullPath, out var contentType))
        contentType = "application/octet-stream";

    return Results.File(fullPath, contentType);
});

// --------------------------------------------------
// Middleware
// --------------------------------------------------
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "LawAfrica.API v1");
    c.RoutePrefix = "swagger";
});

// ✅ Routing is required for correct endpoint routing + CORS behavior
app.UseRouting();

// ✅ CORS after routing so it applies to controller endpoints properly
app.UseCors("ViteDev");

// ✅ Handle ALL preflight OPTIONS requests (fixes CORS “blocked” for some endpoints)
app.MapMethods("{*path}", new[] { "OPTIONS" }, () => Results.Ok())
   .RequireCors("ViteDev");

app.UseAuthentication();
app.UseAuthorization();

// ✅ Ensure controllers always get the CORS policy too
app.MapControllers().RequireCors("ViteDev");

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "LawAfrica.API" }));
app.MapGet("/health", () => Results.Ok("ok"));

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    app.Urls.Add($"http://0.0.0.0:{port}");
}

try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Database migration failed on startup.");
}

app.Run();
