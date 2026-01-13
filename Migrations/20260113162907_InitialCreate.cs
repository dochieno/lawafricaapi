using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminPermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Action = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: false),
                    PerformedByUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    AccessModel = table.Column<int>(type: "integer", nullable: false),
                    InstitutionAccessModel = table.Column<int>(type: "integer", nullable: false),
                    PublicAccessModel = table.Column<int>(type: "integer", nullable: false),
                    IncludedInInstitutionBundle = table.Column<bool>(type: "boolean", nullable: false),
                    IncludedInPublicBundle = table.Column<bool>(type: "boolean", nullable: false),
                    AvailableToInstitutions = table.Column<bool>(type: "boolean", nullable: false),
                    AvailableToPublic = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsoCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    PhoneCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastNumber = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentProviderTransactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RawJson = table.Column<string>(type: "text", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentProviderTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentProviderWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    DedupeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SignatureValid = table.Column<bool>(type: "boolean", nullable: true),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    ProcessingError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RawBody = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentProviderWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    InstitutionId = table.Column<int>(type: "integer", nullable: true),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    Allowed = table.Column<bool>(type: "boolean", nullable: false),
                    DecisionReason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Surface = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Institutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShortName = table.Column<string>(type: "text", nullable: true),
                    EmailDomain = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    OfficialEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    AlternatePhoneNumber = table.Column<string>(type: "text", nullable: true),
                    AddressLine1 = table.Column<string>(type: "text", nullable: true),
                    AddressLine2 = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    StateOrProvince = table.Column<string>(type: "text", nullable: true),
                    PostalCode = table.Column<string>(type: "text", nullable: true),
                    CountryId = table.Column<int>(type: "integer", nullable: true),
                    RegistrationNumber = table.Column<string>(type: "text", nullable: true),
                    TaxPin = table.Column<string>(type: "text", nullable: true),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InstitutionType = table.Column<int>(type: "integer", nullable: false),
                    InstitutionAccessCode = table.Column<string>(type: "text", nullable: true),
                    RequiresUserApproval = table.Column<bool>(type: "boolean", nullable: false),
                    MaxStudentSeats = table.Column<int>(type: "integer", nullable: false),
                    MaxStaffSeats = table.Column<int>(type: "integer", nullable: false),
                    AllowIndividualPurchasesWhenInstitutionInactive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Institutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Institutions_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LegalDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Author = table.Column<string>(type: "text", nullable: true),
                    Publisher = table.Column<string>(type: "text", nullable: true),
                    Edition = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    CountryId = table.Column<int>(type: "integer", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    FileType = table.Column<string>(type: "text", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PageCount = table.Column<int>(type: "integer", nullable: true),
                    ChapterCount = table.Column<int>(type: "integer", nullable: true),
                    IsPremium = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CoverImagePath = table.Column<string>(type: "text", nullable: true),
                    FileHashSha256 = table.Column<string>(type: "text", nullable: true),
                    LastIndexedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TableOfContentsJson = table.Column<string>(type: "text", nullable: true),
                    ContentProductId = table.Column<int>(type: "integer", nullable: true),
                    PublicPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    PublicCurrency = table.Column<string>(type: "text", nullable: true),
                    AllowPublicPurchase = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalDocuments_ContentProducts_ContentProductId",
                        column: x => x.ContentProductId,
                        principalTable: "ContentProducts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LegalDocuments_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InstitutionProductSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InstitutionId = table.Column<int>(type: "integer", nullable: false),
                    ContentProductId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionProductSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstitutionProductSubscriptions_ContentProducts_ContentProd~",
                        column: x => x.ContentProductId,
                        principalTable: "ContentProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InstitutionProductSubscriptions_Institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalTable: "Institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RegistrationIntents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    CountryId = table.Column<int>(type: "integer", nullable: true),
                    UserType = table.Column<int>(type: "integer", nullable: false),
                    InstitutionId = table.Column<int>(type: "integer", nullable: true),
                    PaymentCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InstitutionAccessCode = table.Column<string>(type: "text", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsConsumed = table.Column<bool>(type: "boolean", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InstitutionMemberType = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationIntents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegistrationIntents_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RegistrationIntents_Institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalTable: "Institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    UserType = table.Column<int>(type: "integer", nullable: false),
                    InstitutionId = table.Column<int>(type: "integer", nullable: true),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: true),
                    FirstName = table.Column<string>(type: "text", nullable: true),
                    LastName = table.Column<string>(type: "text", nullable: true),
                    CountryId = table.Column<int>(type: "integer", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    ProfileImageUrl = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    IsEmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    EmailVerificationToken = table.Column<string>(type: "text", nullable: true),
                    EmailVerificationTokenExpiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsPhoneVerified = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorSecret = table.Column<string>(type: "text", nullable: true),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorCode = table.Column<string>(type: "text", nullable: true),
                    TwoFactorExpiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordResetToken = table.Column<string>(type: "text", nullable: true),
                    PasswordResetTokenExpiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedLoginAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockoutEndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsGlobalAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorSetupTokenHash = table.Column<string>(type: "text", nullable: true),
                    TwoFactorSetupTokenExpiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_Institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalTable: "Institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ContentProductLegalDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContentProductId = table.Column<int>(type: "integer", nullable: false),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentProductLegalDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentProductLegalDocuments_ContentProducts_ContentProduct~",
                        column: x => x.ContentProductId,
                        principalTable: "ContentProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentProductLegalDocuments_LegalDocuments_LegalDocumentId",
                        column: x => x.LegalDocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTextIndexes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    IndexedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTextIndexes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentTextIndexes_LegalDocuments_LegalDocumentId",
                        column: x => x.LegalDocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LegalDocumentNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    NodeType = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalDocumentNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalDocumentNodes_LegalDocumentNodes_ParentId",
                        column: x => x.ParentId,
                        principalTable: "LegalDocumentNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegalDocumentNodes_LegalDocuments_LegalDocumentId",
                        column: x => x.LegalDocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLibraries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    AccessType = table.Column<int>(type: "integer", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLibraries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserLibraries_LegalDocuments_LegalDocumentId",
                        column: x => x.LegalDocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstitutionSubscriptionActionRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubscriptionId = table.Column<int>(type: "integer", nullable: false),
                    RequestType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    RequestNotes = table.Column<string>(type: "text", nullable: true),
                    ReviewNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionSubscriptionActionRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstitutionSubscriptionActionRequests_InstitutionProductSub~",
                        column: x => x.SubscriptionId,
                        principalTable: "InstitutionProductSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstitutionSubscriptionAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubscriptionId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    PerformedByUserId = table.Column<int>(type: "integer", nullable: true),
                    OldStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OldEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OldStatus = table.Column<int>(type: "integer", nullable: false),
                    NewStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NewEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionSubscriptionAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstitutionSubscriptionAudits_InstitutionProductSubscriptio~",
                        column: x => x.SubscriptionId,
                        principalTable: "InstitutionProductSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstitutionMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InstitutionId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    MemberType = table.Column<int>(type: "integer", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdminNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstitutionMemberships_Institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalTable: "Institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InstitutionMemberships_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InstitutionMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    InstitutionId = table.Column<int>(type: "integer", nullable: true),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxTotal = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "numeric", nullable: false),
                    Total = table.Column<decimal>(type: "numeric", nullable: false),
                    AmountPaid = table.Column<decimal>(type: "numeric", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalInvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalTable: "Institutions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Invoices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LegalDocumentAnnotations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: true),
                    Cfi = table.Column<string>(type: "text", nullable: true),
                    StartCharOffset = table.Column<int>(type: "integer", nullable: true),
                    EndCharOffset = table.Column<int>(type: "integer", nullable: true),
                    SelectedText = table.Column<string>(type: "text", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    Color = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalDocumentAnnotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalDocumentAnnotations_LegalDocuments_LegalDocumentId",
                        column: x => x.LegalDocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LegalDocumentAnnotations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LegalDocumentNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    HighlightedText = table.Column<string>(type: "text", nullable: true),
                    PageNumber = table.Column<int>(type: "integer", nullable: true),
                    CharOffsetStart = table.Column<int>(type: "integer", nullable: true),
                    CharOffsetEnd = table.Column<int>(type: "integer", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Chapter = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HighlightColor = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalDocumentNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalDocumentNotes_LegalDocuments_LegalDocumentId",
                        column: x => x.LegalDocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LegalDocumentNotes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LegalDocumentProgress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: true),
                    Cfi = table.Column<string>(type: "text", nullable: true),
                    CharOffset = table.Column<int>(type: "integer", nullable: true),
                    Percentage = table.Column<double>(type: "double precision", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    TotalSecondsRead = table.Column<int>(type: "integer", nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalDocumentProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalDocumentProgress_LegalDocuments_LegalDocumentId",
                        column: x => x.LegalDocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LegalDocumentProgress_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoginAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: false),
                    UserAgent = table.Column<string>(type: "text", nullable: false),
                    UserName = table.Column<string>(type: "text", nullable: false),
                    LoggedInAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsSuccessful = table.Column<bool>(type: "boolean", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoginAudits_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PaymentReconciliationRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<int>(type: "integer", nullable: true),
                    FromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PerformedByUserId = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReconciliationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentReconciliationRuns_Users_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAdminPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PermissionId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAdminPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAdminPermissions_AdminPermissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "AdminPermissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserAdminPermissions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLegalDocumentPurchases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    PaymentReference = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLegalDocumentPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserLegalDocumentPurchases_LegalDocuments_LegalDocumentId",
                        column: x => x.LegalDocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserLegalDocumentPurchases_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProductOwnerships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ContentProductId = table.Column<int>(type: "integer", nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TransactionReference = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProductOwnerships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserProductOwnerships_ContentProducts_ContentProductId",
                        column: x => x.ContentProductId,
                        principalTable: "ContentProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserProductOwnerships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProductSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ContentProductId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProductSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserProductSubscriptions_ContentProducts_ContentProductId",
                        column: x => x.ContentProductId,
                        principalTable: "ContentProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserProductSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    LineSubtotal = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric", nullable: false),
                    ContentProductId = table.Column<int>(type: "integer", nullable: true),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentIntents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    InstitutionId = table.Column<int>(type: "integer", nullable: true),
                    RegistrationIntentId = table.Column<int>(type: "integer", nullable: true),
                    ContentProductId = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    MerchantRequestId = table.Column<string>(type: "text", nullable: true),
                    CheckoutRequestId = table.Column<string>(type: "text", nullable: true),
                    MpesaReceiptNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    ProviderResultCode = table.Column<string>(type: "text", nullable: true),
                    ProviderResultDesc = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ManualReference = table.Column<string>(type: "text", nullable: true),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    AdminNotes = table.Column<string>(type: "text", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFinalized = table.Column<bool>(type: "boolean", nullable: false),
                    DurationInMonths = table.Column<int>(type: "integer", nullable: true),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: true),
                    ProviderReference = table.Column<string>(type: "text", nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "text", nullable: true),
                    ProviderChannel = table.Column<string>(type: "text", nullable: true),
                    ProviderPaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderRawJson = table.Column<string>(type: "text", nullable: true),
                    InvoiceId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentIntents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentIntents_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentIntents_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentReconciliationItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    PaymentIntentId = table.Column<int>(type: "integer", nullable: true),
                    ProviderTransactionIdRef = table.Column<long>(type: "bigint", nullable: true),
                    ProviderTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    InvoiceId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReconciliationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentReconciliationItems_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentReconciliationItems_PaymentIntents_PaymentIntentId",
                        column: x => x.PaymentIntentId,
                        principalTable: "PaymentIntents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentReconciliationItems_PaymentProviderTransactions_Prov~",
                        column: x => x.ProviderTransactionId,
                        principalTable: "PaymentProviderTransactions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentReconciliationItems_PaymentReconciliationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PaymentReconciliationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Countries",
                columns: new[] { "Id", "IsoCode", "Name", "PhoneCode" },
                values: new object[,]
                {
                    { 1, "KE", "Kenya", "+254" },
                    { 2, "UG", "Uganda", "+256" },
                    { 3, "TZ", "Tanzania", "+255" },
                    { 4, "RW", "Rwanda", "+250" },
                    { 5, "ZA", "South Africa", "+27" }
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Admin" },
                    { 2, "User" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminPermissions_Code",
                table: "AdminPermissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentProductLegalDocuments_ContentProductId_LegalDocument~",
                table: "ContentProductLegalDocuments",
                columns: new[] { "ContentProductId", "LegalDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentProductLegalDocuments_LegalDocumentId",
                table: "ContentProductLegalDocuments",
                column: "LegalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTextIndexes_LegalDocumentId_PageNumber",
                table: "DocumentTextIndexes",
                columns: new[] { "LegalDocumentId", "PageNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionMemberships_ApprovedByUserId",
                table: "InstitutionMemberships",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionMemberships_InstitutionId_ReferenceNumber",
                table: "InstitutionMemberships",
                columns: new[] { "InstitutionId", "ReferenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionMemberships_UserId_InstitutionId",
                table: "InstitutionMemberships",
                columns: new[] { "UserId", "InstitutionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProductSubscriptions_ContentProductId",
                table: "InstitutionProductSubscriptions",
                column: "ContentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProductSubscriptions_EndDate",
                table: "InstitutionProductSubscriptions",
                column: "EndDate");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProductSubscriptions_InstitutionId_ContentProduc~",
                table: "InstitutionProductSubscriptions",
                columns: new[] { "InstitutionId", "ContentProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProductSubscriptions_StartDate",
                table: "InstitutionProductSubscriptions",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProductSubscriptions_Status_EndDate",
                table: "InstitutionProductSubscriptions",
                columns: new[] { "Status", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProductSubscriptions_Status_StartDate",
                table: "InstitutionProductSubscriptions",
                columns: new[] { "Status", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Institutions_CountryId",
                table: "Institutions",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Institutions_EmailDomain",
                table: "Institutions",
                column: "EmailDomain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Institutions_InstitutionAccessCode",
                table: "Institutions",
                column: "InstitutionAccessCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Institutions_Name",
                table: "Institutions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Institutions_RegistrationNumber",
                table: "Institutions",
                column: "RegistrationNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionSubscriptionActionRequests_Status_CreatedAt",
                table: "InstitutionSubscriptionActionRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionSubscriptionActionRequests_SubscriptionId_Status",
                table: "InstitutionSubscriptionActionRequests",
                columns: new[] { "SubscriptionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionSubscriptionAudits_SubscriptionId_CreatedAt",
                table: "InstitutionSubscriptionAudits",
                columns: new[] { "SubscriptionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId",
                table: "InvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InstitutionId",
                table: "Invoices",
                column: "InstitutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceNumber",
                table: "Invoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_IssuedAt",
                table: "Invoices",
                column: "IssuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Status",
                table: "Invoices",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_UserId",
                table: "Invoices",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSequences_Year",
                table: "InvoiceSequences",
                column: "Year",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentAnnotations_LegalDocumentId",
                table: "LegalDocumentAnnotations",
                column: "LegalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentAnnotations_UserId_LegalDocumentId",
                table: "LegalDocumentAnnotations",
                columns: new[] { "UserId", "LegalDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentNodes_LegalDocumentId",
                table: "LegalDocumentNodes",
                column: "LegalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentNodes_ParentId",
                table: "LegalDocumentNodes",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentNotes_LegalDocumentId",
                table: "LegalDocumentNotes",
                column: "LegalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentNotes_UserId",
                table: "LegalDocumentNotes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentProgress_LegalDocumentId",
                table: "LegalDocumentProgress",
                column: "LegalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentProgress_UserId_LegalDocumentId",
                table: "LegalDocumentProgress",
                columns: new[] { "UserId", "LegalDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocuments_ContentProductId",
                table: "LegalDocuments",
                column: "ContentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocuments_CountryId",
                table: "LegalDocuments",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAudits_UserId",
                table: "LoginAudits",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_ApprovedByUserId",
                table: "PaymentIntents",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_CheckoutRequestId",
                table: "PaymentIntents",
                column: "CheckoutRequestId",
                unique: true,
                filter: "\"CheckoutRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_InvoiceId",
                table: "PaymentIntents",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_Provider_ProviderReference",
                table: "PaymentIntents",
                columns: new[] { "Provider", "ProviderReference" },
                unique: true,
                filter: "\"ProviderReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_Provider_ProviderTransactionId",
                table: "PaymentIntents",
                columns: new[] { "Provider", "ProviderTransactionId" },
                unique: true,
                filter: "\"ProviderTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderTransactions_PaidAt",
                table: "PaymentProviderTransactions",
                column: "PaidAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderTransactions_Provider_ProviderTransactionId",
                table: "PaymentProviderTransactions",
                columns: new[] { "Provider", "ProviderTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderTransactions_Provider_Reference",
                table: "PaymentProviderTransactions",
                columns: new[] { "Provider", "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderWebhookEvents_Provider_DedupeHash",
                table: "PaymentProviderWebhookEvents",
                columns: new[] { "Provider", "DedupeHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderWebhookEvents_ReceivedAt",
                table: "PaymentProviderWebhookEvents",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderWebhookEvents_Reference",
                table: "PaymentProviderWebhookEvents",
                column: "Reference");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationItems_InvoiceId",
                table: "PaymentReconciliationItems",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationItems_PaymentIntentId",
                table: "PaymentReconciliationItems",
                column: "PaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationItems_Provider_Reference",
                table: "PaymentReconciliationItems",
                columns: new[] { "Provider", "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationItems_ProviderTransactionId",
                table: "PaymentReconciliationItems",
                column: "ProviderTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationItems_Reason",
                table: "PaymentReconciliationItems",
                column: "Reason");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationItems_RunId",
                table: "PaymentReconciliationItems",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationItems_Status",
                table: "PaymentReconciliationItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationRuns_CreatedAt",
                table: "PaymentReconciliationRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationRuns_PerformedByUserId",
                table: "PaymentReconciliationRuns",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationIntents_CountryId",
                table: "RegistrationIntents",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationIntents_Email",
                table: "RegistrationIntents",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationIntents_InstitutionId_ReferenceNumber",
                table: "RegistrationIntents",
                columns: new[] { "InstitutionId", "ReferenceNumber" },
                unique: true,
                filter: "\"InstitutionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationIntents_ReferenceNumber",
                table: "RegistrationIntents",
                column: "ReferenceNumber",
                unique: true,
                filter: "\"InstitutionId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationIntents_Username",
                table: "RegistrationIntents",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_AtUtc",
                table: "UsageEvents",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_AtUtc_Allowed",
                table: "UsageEvents",
                columns: new[] { "AtUtc", "Allowed" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_InstitutionId_AtUtc",
                table: "UsageEvents",
                columns: new[] { "InstitutionId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_LegalDocumentId_AtUtc",
                table: "UsageEvents",
                columns: new[] { "LegalDocumentId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAdminPermissions_PermissionId",
                table: "UserAdminPermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAdminPermissions_UserId_PermissionId",
                table: "UserAdminPermissions",
                columns: new[] { "UserId", "PermissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserLegalDocumentPurchases_LegalDocumentId",
                table: "UserLegalDocumentPurchases",
                column: "LegalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLegalDocumentPurchases_UserId_LegalDocumentId",
                table: "UserLegalDocumentPurchases",
                columns: new[] { "UserId", "LegalDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserLibraries_LegalDocumentId",
                table: "UserLibraries",
                column: "LegalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLibraries_UserId_LegalDocumentId",
                table: "UserLibraries",
                columns: new[] { "UserId", "LegalDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProductOwnerships_ContentProductId",
                table: "UserProductOwnerships",
                column: "ContentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProductOwnerships_UserId",
                table: "UserProductOwnerships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProductSubscriptions_ContentProductId",
                table: "UserProductSubscriptions",
                column: "ContentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProductSubscriptions_UserId",
                table: "UserProductSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_CountryId",
                table: "Users",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_InstitutionId",
                table: "Users",
                column: "InstitutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "ContentProductLegalDocuments");

            migrationBuilder.DropTable(
                name: "DocumentTextIndexes");

            migrationBuilder.DropTable(
                name: "InstitutionMemberships");

            migrationBuilder.DropTable(
                name: "InstitutionSubscriptionActionRequests");

            migrationBuilder.DropTable(
                name: "InstitutionSubscriptionAudits");

            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "InvoiceSequences");

            migrationBuilder.DropTable(
                name: "LegalDocumentAnnotations");

            migrationBuilder.DropTable(
                name: "LegalDocumentNodes");

            migrationBuilder.DropTable(
                name: "LegalDocumentNotes");

            migrationBuilder.DropTable(
                name: "LegalDocumentProgress");

            migrationBuilder.DropTable(
                name: "LoginAudits");

            migrationBuilder.DropTable(
                name: "PaymentProviderWebhookEvents");

            migrationBuilder.DropTable(
                name: "PaymentReconciliationItems");

            migrationBuilder.DropTable(
                name: "RegistrationIntents");

            migrationBuilder.DropTable(
                name: "UsageEvents");

            migrationBuilder.DropTable(
                name: "UserAdminPermissions");

            migrationBuilder.DropTable(
                name: "UserLegalDocumentPurchases");

            migrationBuilder.DropTable(
                name: "UserLibraries");

            migrationBuilder.DropTable(
                name: "UserProductOwnerships");

            migrationBuilder.DropTable(
                name: "UserProductSubscriptions");

            migrationBuilder.DropTable(
                name: "InstitutionProductSubscriptions");

            migrationBuilder.DropTable(
                name: "PaymentIntents");

            migrationBuilder.DropTable(
                name: "PaymentProviderTransactions");

            migrationBuilder.DropTable(
                name: "PaymentReconciliationRuns");

            migrationBuilder.DropTable(
                name: "AdminPermissions");

            migrationBuilder.DropTable(
                name: "LegalDocuments");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "ContentProducts");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Institutions");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Countries");
        }
    }
}
