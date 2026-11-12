using Microsoft.EntityFrameworkCore;
using VegetableShopAPI.Models;
using Microsoft.EntityFrameworkCore;

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
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
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

       
        }
    }
}