using LawAfrica.API;
using LawAfrica.API.Authorization.Handlers;
using LawAfrica.API.Authorization.Policies;
using LawAfrica.API.Authorization.Requirements;
using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Services;
using LawAfrica.API.Services.Ai;
using LawAfrica.API.Services.Ai.Sections;
using LawAfrica.API.Services.Documents;
using LawAfrica.API.Services.Documents.Indexing;
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
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// --------------------------------------------------
// Options Binding
// --------------------------------------------------
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

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
    throw new InvalidOperationException(
        "EmailSettings:Host is missing or empty (SMTP mode). For Graph, set GraphTenantId/GraphClientId/GraphClientSecret."
    );

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
builder.Services.AddScoped<LegalDocumentTocService>();
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

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// DI
builder.Services.AddScoped<InvoicePdfService>();

// --------------------------------------------------
// Authorization
// --------------------------------------------------
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireUser", policy => policy.RequireRole("User", "Admin"));

    // ✅ Allows either Role=Admin OR token claim isGlobalAdmin=true
    options.AddPolicy("RequireAdminOrGlobalAdmin", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.IsInRole("Admin") ||
            ctx.User.HasClaim(c => (c.Type == "isGlobalAdmin" || c.Type == "IsGlobalAdmin") &&
                                   string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase))));

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
// OpenAI / AI
// --------------------------------------------------
builder.Services.AddSingleton<ChatClient>(_ =>
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    var model = Environment.GetEnvironmentVariable("AI_MODEL") ?? "gpt-4o-mini";

    if (string.IsNullOrWhiteSpace(apiKey))
        throw new InvalidOperationException("OPENAI_API_KEY is missing. Add it to Render env vars.");

    return new ChatClient(model: model, apiKey: apiKey);
});

builder.Services.AddSingleton<ILegalDocumentIndexingQueue, LegalDocumentIndexingQueue>();
builder.Services.AddScoped<ILegalDocumentTextIndexer, PdfPigLegalDocumentTextIndexer>();
builder.Services.AddHostedService<LegalDocumentIndexingWorker>();
builder.Services.AddScoped<ILawReportSummarizer, OpenAiLawReportSummarizer>();
builder.Services.AddScoped<ISectionTextExtractor, SectionTextExtractor>();
builder.Services.AddScoped<ILegalDocumentSectionSummarizer, LegalDocumentSectionSummarizer>();
builder.Services.AddScoped<ILawReportRelatedCasesService, OpenAiLawReportRelatedCasesService>();
builder.Services.AddScoped<ILawReportContentBuilder, LawReportContentBuilder>();
builder.Services.AddHttpClient<ILawReportFormatter, OpenAiLawReportFormatter>();
builder.Services.AddScoped<IAiTextClient, AiTextClientAdapter>();
builder.Services.AddScoped<ILawReportChatService, LawReportChatService>();

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
// ✅ CORS (Cloudflare Pages + local dev) — DEFAULT POLICY (most reliable)
// --------------------------------------------------
builder.Services.AddCors(options =>
{
    // 🔧 CORS FIX: use a NAMED policy so we can force it on OPTIONS + controllers
    options.AddPolicy("ViteDev", policy =>
    {
        policy.WithOrigins(
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
// --------------------------------------------------
var storageRoot = Environment.GetEnvironmentVariable("STORAGE_ROOT");
if (string.IsNullOrWhiteSpace(storageRoot))
{
    storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "Storage");
}
Directory.CreateDirectory(storageRoot);
app.Logger.LogInformation("Storage root: {StorageRoot}", storageRoot);

app.UseStaticFiles();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(storageRoot),
    RequestPath = "/storage"
});

var contentTypes = new FileExtensionContentTypeProvider();
app.MapGet("/storage/{**filePath}", (string filePath) =>
{
    if (string.IsNullOrWhiteSpace(filePath))
        return Results.NotFound();

    var clean = filePath.Replace('\\', '/').TrimStart('/');

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
// Swagger
// --------------------------------------------------
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "LawAfrica.API v1");
    c.RoutePrefix = "swagger";
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// --------------------------------------------------
// ✅ CRITICAL ORDER: Routing -> CORS -> (OPTIONS handler) -> custom middleware -> Auth
// --------------------------------------------------
app.UseRouting();

// 🔧 CORS FIX: explicitly apply named CORS policy
app.UseCors("ViteDev");

// 🔧 CORS FIX: OPTIONS must also explicitly require CORS policy
app.MapMethods("{*path}", new[] { "OPTIONS" }, () => Results.Ok())
   .RequireCors("ViteDev");

// ✅ Custom middleware (can short-circuit; CORS already ran)
app.UseMiddleware<ApiExceptionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

// Paystack misconfig safety-net (root path)
app.MapGet("/return-visit", (HttpContext ctx) =>
{
    var reference = (ctx.Request.Query["reference"].ToString() ?? "").Trim();
    var trxref = (ctx.Request.Query["trxref"].ToString() ?? "").Trim();
    var r = !string.IsNullOrWhiteSpace(reference) ? reference : trxref;

    var target = string.IsNullOrWhiteSpace(r)
        ? "/api/payments/paystack/return"
        : $"/api/payments/paystack/return?reference={Uri.EscapeDataString(r)}";

    return Results.Redirect(target);
});

// 🔧 CORS FIX: force controllers to use the same policy
app.MapControllers().RequireCors("ViteDev");

// Basic endpoints
app.MapGet("/", () => Results.Ok(new { status = "ok", service = "LawAfrica.API" }));
app.MapGet("/health", () => Results.Ok("ok"));

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    app.Urls.Add($"http://0.0.0.0:{port}");
}

// --------------------------------------------------
// Auto-migrate
// --------------------------------------------------
var autoMigrate = Environment.GetEnvironmentVariable("AUTO_MIGRATE");

var shouldMigrate =
    builder.Environment.IsDevelopment()
        ? string.Equals(autoMigrate, "true", StringComparison.OrdinalIgnoreCase)
        : !string.Equals(autoMigrate, "false", StringComparison.OrdinalIgnoreCase);

if (shouldMigrate)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.Migrate();
        app.Logger.LogInformation("Database migration applied on startup. AUTO_MIGRATE={AutoMigrate}", autoMigrate);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database migration failed on startup.");
        throw;
    }
}
else
{
    app.Logger.LogWarning("AUTO_MIGRATE disabled. No migrations applied on startup. Environment={Env}", builder.Environment.EnvironmentName);
}

app.Run();
