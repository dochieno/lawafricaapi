using LawAfrica.API;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Authorization;
using LawAfrica.API.Models.Institutions;
using LawAfrica.API.Models.Payments;
using LawAfrica.API.Models.Usage;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Country> Countries { get; set; }

    public DbSet<Role> Roles { get; set; }
    public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();

    //Legal Documents
    public DbSet<LegalDocument> LegalDocuments => Set<LegalDocument>();

    //Legal Document Nodes
    public DbSet<LegalDocumentNode> LegalDocumentNodes => Set<LegalDocumentNode>();

    //Legal Document Notes
    public DbSet<LegalDocumentNote> LegalDocumentNotes { get; set; }

    //Legal Document Progress
    public DbSet<LegalDocumentProgress> LegalDocumentProgress { get; set; } = null!;

    //User Library
    public DbSet<UserLibrary> UserLibraries { get; set; }

    //Document Text Indexes
    public DbSet<DocumentTextIndex> DocumentTextIndexes { get; set; } = null!;

    //Legal Document Annotations
    public DbSet<LegalDocumentAnnotation> LegalDocumentAnnotations { get; set; } = null!;

    //Institutions
    public DbSet<Institution> Institutions { get; set; }

    //Registration Intents
    public DbSet<RegistrationIntent> RegistrationIntents { get; set; }

    // Audit Events
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
    public DbSet<UserLegalDocumentPurchase> UserLegalDocumentPurchases { get; set; } = null!;
    public DbSet<AdminPermission> AdminPermissions => Set<AdminPermission>();
    public DbSet<UserAdminPermission> UserAdminPermissions => Set<UserAdminPermission>();
    public DbSet<InstitutionSubscriptionActionRequest> InstitutionSubscriptionActionRequests { get; set; } = null!;

    //Invoicing & Payments
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<InvoiceSequence> InvoiceSequences => Set<InvoiceSequence>();
    public DbSet<PaymentProviderWebhookEvent> PaymentProviderWebhookEvents => Set<PaymentProviderWebhookEvent>();
    public DbSet<PaymentProviderTransaction> PaymentProviderTransactions => Set<PaymentProviderTransaction>();
    public DbSet<PaymentReconciliationRun> PaymentReconciliationRuns => Set<PaymentReconciliationRun>();
    public DbSet<PaymentReconciliationItem> PaymentReconciliationItems => Set<PaymentReconciliationItem>();



    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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

        modelBuilder.Entity<LoginAudit>(entity =>
        {
            entity.ToTable("LoginAudits");
            entity.HasKey(x => x.Id);

            entity.HasOne(x => x.User)
                  .WithMany()
                  .HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<InstitutionProductSubscription>()
            .HasIndex(x => new { x.InstitutionId, x.ContentProductId })
            .IsUnique();

        // ✅ Phase 1 performance indexes for auto-status transitions
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

        //Content Products
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

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Username)
                  .IsRequired()
                  .HasMaxLength(100);

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

            entity.HasIndex(u => u.Username).IsUnique();
            entity.HasIndex(u => u.Email).IsUnique();

            entity.HasOne(u => u.Country)
                  .WithMany(c => c.Users)
                  .HasForeignKey(u => u.CountryId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(u => u.Institution)
                  .WithMany(i => i.Users)
                  .HasForeignKey(u => u.InstitutionId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

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

            entity.HasIndex(r => r.Email).IsUnique();

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
        });

        // ✅ MOVED OUTSIDE RegistrationIntent block (IMPORTANT)
        modelBuilder.Entity<PaymentIntent>()
            .HasOne(p => p.ApprovedByUser)
            .WithMany()
            .HasForeignKey(p => p.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PaymentIntent>()
            .HasIndex(p => p.CheckoutRequestId)
            .IsUnique()
            .HasFilter("\"CheckoutRequestId\" IS NOT NULL");

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

        // ✅ ADD: ReferenceNumber must be unique per institution (prevents duplicate student/employee numbers)
        modelBuilder.Entity<InstitutionMembership>()
            .HasIndex(m => new { m.InstitutionId, m.ReferenceNumber })
            .IsUnique();

        modelBuilder.Entity<InstitutionMembership>()
            .HasOne(m => m.ApprovedByUser)
            .WithMany()
            .HasForeignKey(m => m.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

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

        modelBuilder.Entity<RegistrationIntent>(entity =>
        {
            entity.ToTable("RegistrationIntents");

            entity.Property(r => r.Email)
                  .IsRequired()
                  .HasMaxLength(255);

            entity.Property(r => r.Username)
                  .IsRequired()
                  .HasMaxLength(100);

            // ✅ ADD: enforce username uniqueness at intent stage too
            entity.HasIndex(r => r.Username).IsUnique();

            // ✅ ADD: ReferenceNumber is mandatory (your controller already enforces it)
            entity.Property(r => r.ReferenceNumber)
                  .IsRequired()
                  .HasMaxLength(120);

            // ✅ ADD: Unique reference numbers for PUBLIC (InstitutionId is NULL)
            entity.HasIndex(r => r.ReferenceNumber)
                  .IsUnique()
                  .HasFilter("\"InstitutionId\" IS NULL");

            // ✅ ADD: Unique reference numbers PER INSTITUTION (InstitutionId is NOT NULL)
            entity.HasIndex(r => new { r.InstitutionId, r.ReferenceNumber })
                  .IsUnique()
                  .HasFilter("\"InstitutionId\" IS NOT NULL");

            entity.Property(r => r.FirstName)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(r => r.LastName)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(r => r.PasswordHash)
                  .IsRequired();

            entity.HasIndex(r => r.Email).IsUnique();

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
        });

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

        //Additional Payment
        modelBuilder.Entity<PaymentIntent>(b =>
        {
            // ✅ Paystack / provider-agnostic idempotency & matching
            b.HasIndex(x => new { x.Provider, x.ProviderReference })
                .IsUnique()
                .HasFilter("\"ProviderReference\" IS NOT NULL");

            b.HasIndex(x => new { x.Provider, x.ProviderTransactionId })
                .IsUnique()
                .HasFilter("\"ProviderTransactionId\" IS NOT NULL");

            // Optional: you already query by Mpesa checkout request id
            b.HasIndex(x => x.CheckoutRequestId);
        });


        //Invoicing
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
        });

        modelBuilder.Entity<InvoiceSequence>(b =>
        {
            b.HasIndex(x => x.Year).IsUnique();
        });

        modelBuilder.Entity<PaymentIntent>(b =>
        {
            b.HasIndex(x => x.InvoiceId);
        });


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
