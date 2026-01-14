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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------
// Configuration
// --------------------------------------------------
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

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

if (string.IsNullOrWhiteSpace(emailSettings.Host))
    throw new InvalidOperationException("EmailSettings:Host is missing or empty.");

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

    options.AddPolicy(PolicyNames.CanAccessLegalDocuments,
        policy => policy.Requirements.Add(new CanAccessLegalDocumentRequirement()));

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
builder.Services.AddScoped<IAuthorizationHandler, CanAccessLegalDocumentHandler>();
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

    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = new List<string>()
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
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
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

var storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "Storage");
Directory.CreateDirectory(storageRoot);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(storageRoot),
    RequestPath = "/storage"
});

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
