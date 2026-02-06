using LawAfrica.API;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Ai;
using LawAfrica.API.Models.Ai.Sections;
using LawAfrica.API.Models.Authorization;
using LawAfrica.API.Models.Documents;
using LawAfrica.API.Models.Emails;
using LawAfrica.API.Models.Institutions;
using LawAfrica.API.Models.LawReports.Enums;
using LawAfrica.API.Models.LawReportsContent.Models;
using LawAfrica.API.Models.Locations;
using LawAfrica.API.Models.Payments;
using LawAfrica.API.Models.Registration;
using LawAfrica.API.Models.Reports;
using LawAfrica.API.Models.Tax;
using LawAfrica.API.Models.Usage;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Xml.Linq;

namespace LawAfrica.API.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // =========================================================
    // ✅ Normalized usernames (long-term fix)
    // - Keeps behavior consistent everywhere
    // - Enables fast indexed lookups by NormalizedUsername
    // =========================================================
    public override int SaveChanges()
    {
        NormalizeUsers();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        NormalizeUsers();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void NormalizeUsers()
    {
        foreach (var entry in ChangeTracker.Entries<User>()
                     .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
        {
            var u = entry.Entity;

            // ✅ Trim always
            u.Username = (u.Username ?? "").Trim();

            // ✅ Consistent invariant normalization (recommended)
            u.NormalizedUsername = u.Username.ToUpperInvariant();
        }
    }

    // =========================================================
    // DbSets
    // =========================================================
    public DbSet<User> Users => Set<User>();
    public DbSet<Country> Countries { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();

    public DbSet<LegalDocument> LegalDocuments => Set<LegalDocument>();
    public DbSet<LegalDocumentNode> LegalDocumentNodes => Set<LegalDocumentNode>();
    public DbSet<LegalDocumentNote> LegalDocumentNotes { get; set; }
    public DbSet<LegalDocumentProgress> LegalDocumentProgress { get; set; } = null!;
    public DbSet<UserLibrary> UserLibraries { get; set; }
    public DbSet<DocumentTextIndex> DocumentTextIndexes { get; set; } = null!;
    public DbSet<LegalDocumentAnnotation> LegalDocumentAnnotations { get; set; } = null!;

    public DbSet<Institution> Institutions { get; set; }
    public DbSet<RegistrationIntent> RegistrationIntents { get; set; }

    public DbSet<AuditEvent> AuditEvents { get; set; }
    public DbSet<UsageEvent> UsageEvents { get; set; }

    public DbSet<InstitutionProductSubscription> InstitutionProductSubscriptions { get; set; }
    public DbSet<UserProductSubscription> UserProductSubscriptions { get; set; }
    public DbSet<UserProductOwnership> UserProductOwnerships { get; set; }
    public DbSet<ContentProduct> ContentProducts { get; set; }
    public DbSet<PaymentIntent> PaymentIntents { get; set; }
    public DbSet<InstitutionMembership> InstitutionMemberships { get; set; }
    public DbSet<InstitutionSubscriptionAudit> InstitutionSubscriptionAudits => Set<InstitutionSubscriptionAudit>();
    public DbSet<ContentProductLegalDocument> ContentProductLegalDocuments { get; set; } = null!;
    public DbSet<LegalDocumentTocEntry> LegalDocumentTocEntries => Set<LegalDocumentTocEntry>();
    public DbSet<LegalDocumentPageText> LegalDocumentPageTexts { get; set; } = null!;

    public DbSet<UserLegalDocumentPurchase> UserLegalDocumentPurchases { get; set; } = null!;
    public DbSet<AdminPermission> AdminPermissions => Set<AdminPermission>();
    public DbSet<UserAdminPermission> UserAdminPermissions => Set<UserAdminPermission>();
    public DbSet<InstitutionSubscriptionActionRequest> InstitutionSubscriptionActionRequests { get; set; } = null!;

    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<InvoiceSequence> InvoiceSequences => Set<InvoiceSequence>();
    public DbSet<PaymentProviderWebhookEvent> PaymentProviderWebhookEvents => Set<PaymentProviderWebhookEvent>();
    public DbSet<PaymentProviderTransaction> PaymentProviderTransactions => Set<PaymentProviderTransaction>();
    public DbSet<PaymentReconciliationRun> PaymentReconciliationRuns => Set<PaymentReconciliationRun>();
    public DbSet<PaymentReconciliationItem> PaymentReconciliationItems => Set<PaymentReconciliationItem>();
    public DbSet<RegistrationResumeOtp> RegistrationResumeOtps => Set<RegistrationResumeOtp>();
    public DbSet<RegistrationResumeSession> RegistrationResumeSessions => Set<RegistrationResumeSession>();
    public DbSet<UserPresence> UserPresences { get; set; } = null!;
    public DbSet<InvoiceSettings> InvoiceSettings => Set<InvoiceSettings>();
    public DbSet<VatRate> VatRates => Set<VatRate>();
    public DbSet<VatRule> VatRules => Set<VatRule>();
    public DbSet<AiLegalDocumentSectionSummary> AiLegalDocumentSectionSummaries => Set<AiLegalDocumentSectionSummary>();
    public DbSet<AiDailyAiUsage> AiDailyAiUsages => Set<AiDailyAiUsage>();


    //LawReports
    public DbSet<LawReport> LawReports => Set<LawReport>();
    public DbSet<Town> Towns => Set<Town>();
    public DbSet<LawReportContentBlock> LawReportContentBlocks => Set<LawReportContentBlock>();
    public DbSet<LawReportContentJsonCache> LawReportContentJsonCaches => Set<LawReportContentJsonCache>();

    //AI
    public DbSet<AiLawReportSummary> AiLawReportSummaries => Set<AiLawReportSummary>();
    public DbSet<AiUsage> AiUsages => Set<AiUsage>();
    public DbSet<UserTrialSubscriptionRequest> UserTrialSubscriptionRequests => Set<UserTrialSubscriptionRequest>();
    public DbSet<ContentProductPrice> ContentProductPrices => Set<ContentProductPrice>();
    public DbSet<EmailOutboxMessage> EmailOutboxMessages => Set<EmailOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // =========================================================
        // Seed
        // =========================================================
        modelBuilder.Entity<Country>().HasData(
            new Country { Id = 1, Name = "Kenya", IsoCode = "KE", PhoneCode = "+254" },
            new Country { Id = 2, Name = "Uganda", IsoCode = "UG", PhoneCode = "+256" },
            new Country { Id = 3, Name = "Tanzania", IsoCode = "TZ", PhoneCode = "+255" },
            new Country { Id = 4, Name = "Rwanda", IsoCode = "RW", PhoneCode = "+250" },
            new Country { Id = 5, Name = "South Africa", IsoCode = "ZA", PhoneCode = "+27" }
        );

        modelBuilder.Entity<Role>().HasData(
            new Role { Id = 1, Name = "Admin" },
            new Role { Id = 2, Name = "User" }
        );

        // =========================================================
        // Country
        // =========================================================
        modelBuilder.Entity<Country>(entity =>
        {
            entity.ToTable("Countries");
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Name)
                  .IsRequired()
                  .HasMaxLength(150);

            entity.Property(c => c.IsoCode).HasMaxLength(3);
            entity.Property(c => c.PhoneCode).HasMaxLength(10);
        });

        modelBuilder.Entity<EmailOutboxMessage>()
                    .HasIndex(x => new { x.Status, x.NextAttemptAtUtc });

        modelBuilder.Entity<EmailOutboxMessage>()
                    .HasIndex(x => new { x.Kind, x.InvoiceId });

        modelBuilder.Entity<InvoiceSettings>(b =>
        {
            b.ToTable("InvoiceSettings");
            b.HasKey(x => x.Id);

            b.Property(x => x.CompanyName)
                .HasMaxLength(200)
                .IsRequired();

            b.Property(x => x.AddressLine1).HasMaxLength(200);
            b.Property(x => x.AddressLine2).HasMaxLength(200);
            b.Property(x => x.City).HasMaxLength(120);
            b.Property(x => x.Country).HasMaxLength(120);
            b.Property(x => x.VatOrPin).HasMaxLength(80);
            b.Property(x => x.Email).HasMaxLength(200);
            b.Property(x => x.Phone).HasMaxLength(80);

            b.Property(x => x.LogoPath).HasMaxLength(300);
            b.Property(x => x.FooterNotes).HasMaxLength(2000);

            // keep DB default
            b.Property(x => x.UpdatedAt)
                .HasDefaultValueSql("timezone('utc', now())");
        });

        modelBuilder.Entity<UserTrialSubscriptionRequest>()
                    .HasIndex(x => new { x.UserId, x.ContentProductId, x.Status });

        modelBuilder.Entity<UserTrialSubscriptionRequest>()
                    .HasOne(x => x.ReviewedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.ReviewedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);


        modelBuilder.Entity<Town>(b =>
        {
            b.ToTable("Towns");
            b.Property(x => x.PostCode).HasMaxLength(20).IsRequired();
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();

            // ✅ Uniqueness per country
            b.HasIndex(x => new { x.CountryId, x.PostCode }).IsUnique();
            b.HasIndex(x => new { x.CountryId, x.Name }); // NOT unique
            b.HasOne(x => x.Country)
                .WithMany()
                .HasForeignKey(x => x.CountryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ✅ LawReport -> Town optional relationship (NO breaking)
        modelBuilder.Entity<LawReport>(b =>
        {
            b.HasOne(x => x.TownRef)
                .WithMany()
                .HasForeignKey(x => x.TownId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasIndex(x => x.TownId);
        });
        // ...

            modelBuilder.Entity<LawReportContentBlock>(e =>
            {
                e.ToTable("LawReportContentBlocks");

                e.HasKey(x => x.Id);

                e.Property(x => x.Type)
                    .HasConversion<short>(); // enum stored as smallint

                e.Property(x => x.Text)
                    .HasMaxLength(20000);

                // store as jsonb in Postgres (still a string in C#)
                e.Property(x => x.Json)
                    .HasColumnType("jsonb");

                e.Property(x => x.Style)
                    .HasMaxLength(80);

                e.HasIndex(x => new { x.LawReportId, x.Order })
                    .IsUnique();

                // optional FK to LawReports table (recommended)
                e.HasOne<LawReport>()               // <-- assumes your entity is named LawReport
                    .WithMany()                     // we can add navigation later if you want
                    .HasForeignKey(x => x.LawReportId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<LawReportContentJsonCache>(e =>
            {
                e.ToTable("LawReportContentJsonCaches");

                e.HasKey(x => x.LawReportId); // 1:1 keyed by LawReportId

                e.Property(x => x.Json)
                    .HasColumnType("jsonb");

                e.Property(x => x.Hash)
                    .HasMaxLength(120);

                e.Property(x => x.BuiltBy)
                    .HasMaxLength(120);

                e.HasOne<LawReport>()               // <-- assumes your entity is named LawReport
                    .WithOne()
                    .HasForeignKey<LawReportContentJsonCache>(x => x.LawReportId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

        modelBuilder.Entity<LegalDocumentPageText>()
                    .HasIndex(x => new { x.LegalDocumentId, x.PageNumber })
                    .IsUnique();


        modelBuilder.Entity<RegistrationResumeOtp>(b =>
        {
            b.ToTable("RegistrationResumeOtps");
            b.HasKey(x => x.Id);

            b.HasIndex(x => new { x.EmailNormalized, x.ExpiresAtUtc });
            b.HasIndex(x => new { x.EmailNormalized, x.IsUsed });

            b.Property(x => x.EmailNormalized).HasMaxLength(256).IsRequired();
            b.Property(x => x.CodeHash).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<VatRate>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.RatePercent).HasColumnType("numeric(9,4)");
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(160);
            e.Property(x => x.CountryScope).HasMaxLength(8);
        });

        modelBuilder.Entity<VatRule>(e =>
        {
            e.HasIndex(x => new { x.Purpose, x.CountryCode, x.Priority });
            e.Property(x => x.Purpose).HasMaxLength(64);
            e.Property(x => x.CountryCode).HasMaxLength(8);
            e.HasOne(x => x.VatRate)
             .WithMany()
             .HasForeignKey(x => x.VatRateId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RegistrationResumeSession>(b =>
        {
            b.ToTable("RegistrationResumeSessions");
            b.HasKey(x => x.Id);

            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => new { x.EmailNormalized, x.ExpiresAtUtc });

            b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            b.Property(x => x.EmailNormalized).HasMaxLength(256).IsRequired();
        });

        // =========================================================
        // ContentProductPrice (Pricing Plans)
        // =========================================================
        modelBuilder.Entity<ContentProductPrice>(b =>
        {
            b.ToTable("ContentProductPrices");
            b.HasKey(x => x.Id);

            b.Property(x => x.Audience).HasConversion<short>();
            b.Property(x => x.BillingPeriod).HasConversion<short>();

            b.Property(x => x.Currency)
                .HasMaxLength(10)
                .IsRequired();

            // Money precision (Postgres numeric)
            b.Property(x => x.Amount)
                .HasColumnType("numeric(18,2)");

            b.HasIndex(x => x.ContentProductId);

            // Fast lookup by audience/period/currency
            b.HasIndex(x => new { x.ContentProductId, x.Audience, x.BillingPeriod, x.Currency });

            // Optional: make filtering "active" fast
            b.HasIndex(x => new { x.IsActive, x.EffectiveFromUtc, x.EffectiveToUtc });

            b.HasOne(x => x.ContentProduct)
                .WithMany()
                .HasForeignKey(x => x.ContentProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });


        //AI
        modelBuilder.Entity<AiLawReportSummary>()
                    .HasIndex(x => new { x.LawReportId, x.UserId, x.SummaryType })
                    .IsUnique();

        modelBuilder.Entity<AiUsage>()
                    .HasIndex(x => new { x.UserId, x.PeriodKey })
                    .IsUnique();
        // =========================================================
        // LoginAudit
        // =========================================================
        modelBuilder.Entity<LoginAudit>(entity =>
        {
            entity.ToTable("LoginAudits");
            entity.HasKey(x => x.Id);

            entity.HasOne(x => x.User)
                  .WithMany()
                  .HasForeignKey(x => x.UserId);
        });

        // =========================================================
        // InstitutionProductSubscription indexes
        // =========================================================
        modelBuilder.Entity<InstitutionProductSubscription>()
            .HasIndex(x => new { x.InstitutionId, x.ContentProductId })
            .IsUnique();

        modelBuilder.Entity<InstitutionProductSubscription>()
            .HasIndex(x => new { x.Status, x.StartDate });

        modelBuilder.Entity<InstitutionProductSubscription>()
            .HasIndex(x => new { x.Status, x.EndDate });

        modelBuilder.Entity<InstitutionProductSubscription>()
            .HasIndex(x => x.StartDate);

        modelBuilder.Entity<InstitutionProductSubscription>()
            .HasIndex(x => x.EndDate);

        modelBuilder.Entity<ContentProductLegalDocument>()
            .HasIndex(x => new { x.ContentProductId, x.LegalDocumentId })
            .IsUnique();

        modelBuilder.Entity<UserLegalDocumentPurchase>()
            .HasIndex(x => new { x.UserId, x.LegalDocumentId })
            .IsUnique();

        // =========================================================
        // ContentProductLegalDocument relations
        // =========================================================
        modelBuilder.Entity<ContentProductLegalDocument>()
            .HasOne(x => x.ContentProduct)
            .WithMany(p => p.ProductDocuments)
            .HasForeignKey(x => x.ContentProductId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ContentProductLegalDocument>()
            .HasOne(x => x.LegalDocument)
            .WithMany(d => d.ProductDocuments)
            .HasForeignKey(x => x.LegalDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InstitutionSubscriptionAudit>()
            .HasOne(a => a.Subscription)
            .WithMany()
            .HasForeignKey(a => a.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InstitutionSubscriptionAudit>()
            .HasIndex(a => new { a.SubscriptionId, a.CreatedAt });

        modelBuilder.Entity<UserProductSubscription>()
                      .HasIndex(x => new { x.UserId, x.ContentProductId })
                      .IsUnique();

        modelBuilder.Entity<UserProductSubscription>()
                  .HasOne(x => x.GrantedByUser)
                  .WithMany()
                  .HasForeignKey(x => x.GrantedByUserId)
                  .OnDelete(DeleteBehavior.Restrict);
        // =========================================================
        // LegalDocument
        // =========================================================
        modelBuilder.Entity<LegalDocument>()
            .HasOne(d => d.Country)
            .WithMany()
            .HasForeignKey(d => d.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LegalDocumentNode>()
            .HasOne(n => n.Parent)
            .WithMany(n => n.Children)
            .HasForeignKey(n => n.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LegalDocumentNode>()
            .HasOne(n => n.LegalDocument)
            .WithMany()
            .HasForeignKey(n => n.LegalDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LegalDocument>()
            .HasOne(x => x.VatRate)
            .WithMany()
            .HasForeignKey(x => x.VatRateId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserLibrary>()
            .HasIndex(x => new { x.UserId, x.LegalDocumentId })
            .IsUnique();

        modelBuilder.Entity<LegalDocumentProgress>()
            .HasIndex(x => new { x.UserId, x.LegalDocumentId })
            .IsUnique();

        modelBuilder.Entity<DocumentTextIndex>()
            .HasIndex(x => new { x.LegalDocumentId, x.PageNumber });

        modelBuilder.Entity<DocumentTextIndex>()
            .Property(x => x.Text)
            .IsRequired();

        modelBuilder.Entity<LegalDocumentAnnotation>()
            .HasIndex(x => new { x.UserId, x.LegalDocumentId });

        modelBuilder.Entity<LegalDocumentAnnotation>()
            .Property(x => x.Type)
            .HasMaxLength(20);

        // =========================================================
        // ✅ User (NormalizedUsername + index)
        // NOTE: Keep your existing Username unique index for now.
        // =========================================================
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Username)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(u => u.NormalizedUsername)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(u => u.Email)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(u => u.PhoneNumber)
                  .IsRequired()
                  .HasMaxLength(50);

            entity.Property(u => u.PasswordHash)
                  .IsRequired();

            entity.Property(u => u.Role)
                  .IsRequired()
                  .HasMaxLength(50);

            // Existing uniqueness (keep)
            entity.HasIndex(u => u.Username).IsUnique();
            entity.HasIndex(u => u.Email).IsUnique();

            // ✅ Long-term: case-insensitive uniqueness + fast lookup
            entity.HasIndex(u => u.NormalizedUsername).IsUnique();

            entity.HasOne(u => u.Country)
                  .WithMany(c => c.Users)
                  .HasForeignKey(u => u.CountryId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(u => u.Institution)
                  .WithMany(i => i.Users)
                  .HasForeignKey(u => u.InstitutionId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // =========================================================
        // Institution
        // =========================================================
        modelBuilder.Entity<Institution>(entity =>
        {
            entity.ToTable("Institutions");

            entity.Property(i => i.Name)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(i => i.EmailDomain)
                  .IsRequired()
                  .HasMaxLength(150);

            entity.Property(i => i.OfficialEmail)
                  .IsRequired()
                  .HasMaxLength(255);

            entity.HasIndex(i => i.InstitutionAccessCode).IsUnique();
            entity.HasIndex(i => i.RegistrationNumber).IsUnique();

            entity.HasIndex(i => i.EmailDomain).IsUnique();
            entity.HasIndex(i => i.Name).IsUnique();

            entity.HasOne(i => i.Country)
                  .WithMany()
                  .HasForeignKey(i => i.CountryId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.Property(i => i.CreatedAt)
                  .HasDefaultValueSql("timezone('utc', now())")
                  .ValueGeneratedOnAdd();
        });

        // =========================================================
        // ✅ RegistrationIntent (merged: you had this twice)
        // =========================================================
        modelBuilder.Entity<RegistrationIntent>(entity =>
        {
            entity.ToTable("RegistrationIntents");

            entity.Property(r => r.Email)
                  .IsRequired()
                  .HasMaxLength(255);

            entity.Property(r => r.Username)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(r => r.FirstName)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(r => r.LastName)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(r => r.PasswordHash)
                  .IsRequired();

            // Existing + your added constraints
            entity.HasIndex(r => r.Email).IsUnique();
            entity.HasIndex(r => r.Username).IsUnique();

            entity.HasOne(r => r.Country)
                  .WithMany()
                  .HasForeignKey(r => r.CountryId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Institution)
                  .WithMany()
                  .HasForeignKey(r => r.InstitutionId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.Property(r => r.CreatedAt)
                  .HasDefaultValueSql("timezone('utc', now())");

            // ...keep existing required fields

            entity.Property(r => r.ReferenceNumber)
                  .HasMaxLength(120);  // nullable by default


        });

        // =========================================================
        // PaymentIntent (existing)
        // =========================================================
        modelBuilder.Entity<PaymentIntent>()
            .HasOne(p => p.ApprovedByUser)
            .WithMany()
            .HasForeignKey(p => p.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PaymentIntent>()
            .HasIndex(p => p.CheckoutRequestId)
            .IsUnique()
            .HasFilter("\"CheckoutRequestId\" IS NOT NULL");

        // Additional Payment (existing)
        modelBuilder.Entity<PaymentIntent>(b =>
        {
            b.HasIndex(x => new { x.Provider, x.ProviderReference })
                .IsUnique()
                .HasFilter("\"ProviderReference\" IS NOT NULL");

            b.HasIndex(x => new { x.Provider, x.ProviderTransactionId })
                .IsUnique()
                .HasFilter("\"ProviderTransactionId\" IS NOT NULL");

            b.HasIndex(x => x.CheckoutRequestId);
            b.HasIndex(x => x.InvoiceId);
            b.HasIndex(x => x.ContentProductPriceId);

        });

        //TOCLegalDocument
        modelBuilder.Entity<LegalDocumentTocEntry>(e =>
        {
            e.ToTable("LegalDocumentTocEntries");

            e.HasOne(x => x.LegalDocument)
                .WithMany(d => d.TocEntries)
                .HasForeignKey(x => x.LegalDocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Helpful indexes
            e.HasIndex(x => new { x.LegalDocumentId, x.ParentId, x.Order });

            e.HasIndex(x => new { x.LegalDocumentId, x.AnchorId });

            // Optional safety: keep title length sane
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.AnchorId).HasMaxLength(200);
            e.Property(x => x.PageLabel).HasMaxLength(50);
            e.Property(x => x.Notes).HasMaxLength(2000);
        });

        //LawReports

        modelBuilder.Entity<LegalDocument>()
                    .Property(x => x.Kind)
                    .HasConversion<int>()
                    .HasDefaultValue(LegalDocumentKind.Standard);

            modelBuilder.Entity<LawReport>(entity =>
            {
                entity.ToTable("LawReports");
                entity.HasKey(x => x.Id);

                // 1:1 unique FK
                entity.HasOne(x => x.LegalDocument)
                    .WithOne(d => d.LawReport)
                    .HasForeignKey<LawReport>(x => x.LegalDocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(x => x.LegalDocumentId).IsUnique();
                // ✅ Citation unique (NULLs allowed multiple times in Postgres)
                entity.HasIndex(x => x.Citation).IsUnique();
                entity.HasIndex(x => new { x.ReportNumber, x.Year, x.CaseNumber });
                entity.Property(x => x.ReportNumber).IsRequired().HasMaxLength(30);
                entity.Property(x => x.Citation).HasMaxLength(120);
                entity.Property(x => x.CaseNumber).HasMaxLength(120);
                entity.Property(x => x.Court).HasMaxLength(200);
                entity.Property(x => x.Parties).HasMaxLength(200);
                entity.Property(x => x.Judges).HasMaxLength(2000);

                entity.Property(x => x.ContentText).IsRequired();
            });

        // =========================================================
        // InstitutionMembership
        // =========================================================
        modelBuilder.Entity<InstitutionMembership>()
            .HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<InstitutionMembership>()
            .HasOne(m => m.Institution)
            .WithMany()
            .HasForeignKey(m => m.InstitutionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InstitutionMembership>()
            .HasIndex(m => new { m.UserId, m.InstitutionId })
            .IsUnique();
        modelBuilder.Entity<InstitutionMembership>()
            .HasOne(m => m.ApprovedByUser)
            .WithMany()
            .HasForeignKey(m => m.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // =========================================================
        // Admin permissions
        // =========================================================
        modelBuilder.Entity<AdminPermission>(entity =>
        {
            entity.ToTable("AdminPermissions");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Code)
                  .IsRequired()
                  .HasMaxLength(120);

            entity.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<UserAdminPermission>(entity =>
        {
            entity.ToTable("UserAdminPermissions");
            entity.HasKey(x => x.Id);

            entity.HasOne(x => x.User)
                  .WithMany()
                  .HasForeignKey(x => x.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Permission)
                  .WithMany()
                  .HasForeignKey(x => x.PermissionId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new { x.UserId, x.PermissionId }).IsUnique();
        });

        modelBuilder.Entity<InstitutionSubscriptionActionRequest>(entity =>
        {
            entity.ToTable("InstitutionSubscriptionActionRequests");
            entity.HasKey(x => x.Id);

            entity.HasOne(x => x.Subscription)
                  .WithMany()
                  .HasForeignKey(x => x.SubscriptionId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.SubscriptionId, x.Status });
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
        });

        modelBuilder.Entity<AiLegalDocumentSectionSummary>(e =>
        {
            e.HasIndex(x => new
            {
                x.UserId,
                x.LegalDocumentId,
                x.TocEntryId,
                x.StartPage,
                x.EndPage,
                x.Type,
                x.PromptVersion
            })
            .IsUnique();
        });


        modelBuilder.Entity<AiDailyAiUsage>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.DayUtc, x.Feature }).IsUnique();
        });


        // =========================================================
        // UserPresence
        // =========================================================
        modelBuilder.Entity<UserPresence>()
            .HasKey(x => x.UserId);

        modelBuilder.Entity<UserPresence>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // =========================================================
        // UsageEvent
        // =========================================================
        modelBuilder.Entity<UsageEvent>(entity =>
        {
            entity.ToTable("UsageEvents");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.AtUtc);
            entity.HasIndex(x => new { x.AtUtc, x.Allowed });
            entity.HasIndex(x => new { x.InstitutionId, x.AtUtc });
            entity.HasIndex(x => new { x.LegalDocumentId, x.AtUtc });

            entity.Property(x => x.DecisionReason).HasMaxLength(120);
            entity.Property(x => x.Surface).HasMaxLength(40);
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.Property(x => x.UserAgent).HasMaxLength(400);
        });

        // =========================================================
        // Invoicing
        // =========================================================
        modelBuilder.Entity<Invoice>(b =>
        {
            b.HasIndex(x => x.InvoiceNumber).IsUnique();
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.IssuedAt);

            b.Property(x => x.InvoiceNumber).HasMaxLength(50);
            b.Property(x => x.Currency).HasMaxLength(10);
            b.Property(x => x.ExternalInvoiceNumber).HasMaxLength(100);
            b.Property(x => x.Notes).HasMaxLength(500);
        });

        modelBuilder.Entity<Invoice>()
            .Property(x => x.CustomerName)
            .HasMaxLength(200);

        modelBuilder.Entity<InvoiceLine>(b =>
        {
            b.HasIndex(x => x.InvoiceId);
            b.Property(x => x.Description).HasMaxLength(200);
            b.Property(x => x.ItemCode).HasMaxLength(50);
            b.HasIndex(x => x.ContentProductPriceId);

        });

        modelBuilder.Entity<InvoiceSequence>(b =>
        {
            b.HasIndex(x => x.Year).IsUnique();
        });

        // =========================================================
        // Payment webhooks / reconciliation
        // =========================================================
        modelBuilder.Entity<PaymentProviderWebhookEvent>(b =>
        {
            b.HasIndex(x => new { x.Provider, x.DedupeHash }).IsUnique();
            b.HasIndex(x => x.ReceivedAt);
            b.HasIndex(x => x.Reference);

            b.Property(x => x.EventType).HasMaxLength(100);
            b.Property(x => x.ProviderEventId).HasMaxLength(150);
            b.Property(x => x.DedupeHash).HasMaxLength(128);
            b.Property(x => x.Reference).HasMaxLength(120);
            b.Property(x => x.ProcessingError).HasMaxLength(500);
        });

        modelBuilder.Entity<PaymentProviderTransaction>(b =>
        {
            b.HasIndex(x => new { x.Provider, x.ProviderTransactionId }).IsUnique();
            b.HasIndex(x => new { x.Provider, x.Reference });
            b.HasIndex(x => x.PaidAt);

            b.Property(x => x.ProviderTransactionId).HasMaxLength(100);
            b.Property(x => x.Reference).HasMaxLength(120);
            b.Property(x => x.Currency).HasMaxLength(10);
            b.Property(x => x.Channel).HasMaxLength(50);
        });

        modelBuilder.Entity<PaymentReconciliationRun>(b =>
        {
            b.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<PaymentReconciliationItem>(b =>
        {
            b.HasIndex(x => new { x.Provider, x.Reference });
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.Reason);

            b.HasOne(x => x.Run)
                .WithMany(r => r.Items)
                .HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Ensure EF picks up these entities (optional but harmless)
        modelBuilder.Entity<InstitutionProductSubscription>();
        modelBuilder.Entity<UserProductSubscription>();
        modelBuilder.Entity<UserProductOwnership>();
        modelBuilder.Entity<ContentProduct>();
    }
}
