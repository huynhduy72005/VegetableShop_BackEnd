using Microsoft.EntityFrameworkCore;
using VegetableShopAPI.Models;

namespace VegetableShopAPI.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Product> Products { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }
        public DbSet<PasswordResetRequest> PasswordResetRequests { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<EmployeeActivityLog> EmployeeActivityLogs { get; set; }

        public DbSet<Voucher> Vouchers { get; set; }
        public DbSet<UserVoucher> UserVouchers { get; set; }
        public object Promotions { get; internal set; }
        public DbSet<ProductVariant> ProductVariants { get; set; }

        // Quản lý lô hàng, xuất hủy & nhà cung cấp
        public DbSet<ProductBatch> ProductBatches { get; set; }
        public DbSet<BatchDisposalLog> BatchDisposalLogs { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }

        // Quản lý Nội dung & Phản hồi khách hàng
        public DbSet<KitchenTip> KitchenTips { get; set; }
        public DbSet<ProductReview> ProductReviews { get; set; }
        public DbSet<ContactMessage> ContactMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.Property(user => user.Username)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.HasIndex(user => user.Username)
                    .IsUnique();

                entity.Property(user => user.IsActive)
                    .HasDefaultValue(true);

                entity.Property(user => user.MustChangePassword)
                    .HasDefaultValue(false);

                entity.Property(user => user.PasswordHash)
                    .IsRequired();
            });

            modelBuilder.Entity<PasswordResetRequest>()
                .Property(request => request.NewPasswordHash)
                .HasMaxLength(500);

            modelBuilder.Entity<Product>()
                .HasOne<Category>()
                .WithMany()
                .HasForeignKey(product => product.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Product>()
                .Property(product => product.IsVisible)
                .HasDefaultValue(true);

            modelBuilder.Entity<Order>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(order => order.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<OrderDetail>()
                .HasOne<Order>()
                .WithMany()
                .HasForeignKey(detail => detail.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<OrderDetail>()
                .HasOne<Product>()
                .WithMany()
                .HasForeignKey(detail => detail.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Employee>(entity =>
            {
                entity.ToTable("Employees");

                entity.HasIndex(e => e.EmployeeCode).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();
                entity.HasIndex(e => e.UserId).IsUnique();

                entity.Property(e => e.CreatedAt)
                    .HasDefaultValueSql("SYSUTCDATETIME()");

                entity.HasOne<User>()
                    .WithOne()
                    .HasForeignKey<Employee>(e => e.UserId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<EmployeeActivityLog>(entity =>
            {
                entity.ToTable("EmployeeActivityLogs");

                entity.HasIndex(e => e.CreatedAt);

                entity.Property(e => e.Action)
                    .HasMaxLength(100);

                entity.Property(e => e.Details)
                    .HasMaxLength(1000);

                entity.Property(e => e.CreatedAt)
                    .HasDefaultValueSql("SYSUTCDATETIME()");

                entity.HasOne<Employee>()
                    .WithMany()
                    .HasForeignKey(e => e.EmployeeId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(e => e.ActorUserId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<ProductVariant>().ToTable("ProductVariants");

            modelBuilder.Entity<Product>()
                .HasMany(p => p.Variants)
                .WithOne(v => v.Product)
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // Cấu hình bảng Suppliers (Nhà cung cấp / Nhà vườn)
            modelBuilder.Entity<Supplier>(entity =>
            {
                entity.ToTable("Suppliers");

                entity.HasIndex(s => s.Name);
                entity.HasIndex(s => s.CertificationType);

                entity.Property(s => s.IsActive)
                    .HasDefaultValue(true);
            });

            // Cấu hình bảng ProductBatches
            modelBuilder.Entity<ProductBatch>(entity =>
            {
                entity.ToTable("ProductBatches");

                entity.HasIndex(b => b.BatchCode);
                entity.HasIndex(b => b.ExpiryDate);
                entity.HasIndex(b => b.SupplierId);

                entity.HasOne(b => b.Product)
                    .WithMany(p => p.Batches)
                    .HasForeignKey(b => b.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(b => b.ProductVariant)
                    .WithMany(v => v.Batches)
                    .HasForeignKey(b => b.ProductVariantId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(b => b.Supplier)
                    .WithMany(s => s.Batches)
                    .HasForeignKey(b => b.SupplierId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Cấu hình bảng BatchDisposalLogs
            modelBuilder.Entity<BatchDisposalLog>(entity =>
            {
                entity.ToTable("BatchDisposalLogs");

                entity.HasIndex(d => d.DisposedAt);

                entity.HasOne(d => d.ProductBatch)
                    .WithMany()
                    .HasForeignKey(d => d.ProductBatchId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Cấu hình bảng KitchenTips (Mẹo hay nhà bếp)
            modelBuilder.Entity<KitchenTip>(entity =>
            {
                entity.ToTable("KitchenTips");

                entity.HasIndex(t => t.Category);
                entity.HasIndex(t => t.IsPublished);

                entity.Property(t => t.IsPublished)
                    .HasDefaultValue(true);
            });

            // Cấu hình bảng ProductReviews (Đánh giá sản phẩm)
            modelBuilder.Entity<ProductReview>(entity =>
            {
                entity.ToTable("ProductReviews");

                entity.HasIndex(r => r.ProductId);
                entity.HasIndex(r => r.Rating);
                entity.HasIndex(r => r.IsHidden);

                entity.HasOne(r => r.Product)
                    .WithMany()
                    .HasForeignKey(r => r.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(r => r.User)
                    .WithMany()
                    .HasForeignKey(r => r.UserId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // Cấu hình bảng ContactMessages (Liên hệ & Khiếu nại)
            modelBuilder.Entity<ContactMessage>(entity =>
            {
                entity.ToTable("ContactMessages");

                entity.HasIndex(c => c.Status);
                entity.HasIndex(c => c.CreatedAt);
            });
        }
    }
}