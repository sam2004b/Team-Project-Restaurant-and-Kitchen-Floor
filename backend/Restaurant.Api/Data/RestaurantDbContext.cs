using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Models;


namespace Restaurant.Api.Data
{
    public class RestaurantDbContext : IdentityDbContext<IdentityUser>
    {
        public RestaurantDbContext(
            DbContextOptions<RestaurantDbContext> options
        ) : base(options)
        {
        }

        public DbSet<Dish> Dishes => Set<Dish>();
        public DbSet<DishIngredient> DishIngredients =>
            Set<DishIngredient>();

        public DbSet<RestaurantAccount> Restaurants =>
            Set<RestaurantAccount>();

        public DbSet<StaffMembership> StaffMemberships =>
            Set<StaffMembership>();

        public DbSet<StaffInvitation> StaffInvitations =>
            Set<StaffInvitation>();


        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Dish>(entity =>
            {
                entity.ToTable("dishes", table =>
                {
                    table.HasCheckConstraint(
                        "CK_dishes_price_positive",
                        "\"Price\" > 0");

                    table.HasCheckConstraint(
                        "CK_dishes_name_not_blank",
                        "length(btrim(\"Name\")) > 0");
                });

                entity.HasKey(d => d.Id);

                entity.Property(d => d.Name)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.Property(d => d.Price)
                    .HasPrecision(10, 2);

                entity.Property(d => d.Category)
                    .HasMaxLength(80);

                entity.Property(d => d.IsAvailable)
                    .HasDefaultValue(true);

                entity.Property(d => d.ImageUrl)
                    .HasMaxLength(500);
            });


            modelBuilder.Entity<DishIngredient>(entity =>
            {
                entity.ToTable("dish_ingredients");

                entity.HasKey(i => i.Id);

                entity.Property(i => i.Name)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.HasOne(i => i.Dish)
                    .WithMany(d => d.Ingredients)
                    .HasForeignKey(i => i.DishId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(i => i.DishId);
            });


            modelBuilder.Entity<RestaurantAccount>(entity =>
            {
                entity.ToTable("restaurants", table =>
                {
                    table.HasCheckConstraint(
                        "CK_restaurants_name_not_blank",
                        "length(btrim(\"Name\")) > 0");

                    table.HasCheckConstraint(
                        "CK_restaurants_address_not_blank",
                        "length(btrim(\"Address\")) > 0");
                });

                entity.HasKey(r => r.Id);

                entity.Property(r => r.Name)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.Property(r => r.Address)
                    .IsRequired()
                    .HasMaxLength(300);

                entity.Property(r => r.NormalizedName)
                    .HasMaxLength(150)
                    .HasComputedColumnSql(
                        "upper(btrim(\"Name\"))",
                        stored: true);

                entity.HasIndex(r => r.NormalizedName)
                    .IsUnique();
            });


            modelBuilder.Entity<StaffMembership>(entity =>
            {
                entity.ToTable("staff_memberships", table =>
                {
                    table.HasCheckConstraint(
                        "CK_staff_memberships_role",
                        "\"Role\" IN ('Administrator', 'Waiter', " +
                        "'Chef', 'Reception Manager')");

                    table.HasCheckConstraint(
                        "CK_staff_memberships_status",
                        "\"Status\" IN ('Active', 'Revoked')");
                });

                entity.HasKey(m => m.Id);

                entity.Property(m => m.UserId)
                    .IsRequired();

                entity.Property(m => m.Role)
                    .IsRequired()
                    .HasMaxLength(32);

                entity.Property(m => m.Status)
                    .HasConversion<string>()
                    .IsRequired()
                    .HasMaxLength(20);

                entity.HasOne(m => m.Restaurant)
                    .WithMany(r => r.StaffMembers)
                    .HasForeignKey(m => m.RestaurantId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(m => m.User)
                    .WithMany()
                    .HasForeignKey(m => m.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(m => new
                {
                    m.RestaurantId,
                    m.UserId
                }).IsUnique();
            });


            modelBuilder.Entity<StaffInvitation>(entity =>
            {
                entity.ToTable("staff_invitations", table =>
                {
                    table.HasCheckConstraint(
                        "CK_staff_invitations_email_not_blank",
                        "length(btrim(\"Email\")) > 0");

                    table.HasCheckConstraint(
                        "CK_staff_invitations_role",
                        "\"Role\" IN ('Administrator', 'Waiter', " +
                        "'Chef', 'Reception Manager')");

                    table.HasCheckConstraint(
                        "CK_staff_invitations_status",
                        "\"Status\" IN ('Pending', 'Accepted', 'Revoked')");
                });

                entity.HasKey(i => i.Id);
                
                entity.Property(i => i.Email)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.Property(i => i.NormalizedEmail)
                    .HasMaxLength(256)
                    .HasComputedColumnSql(
                        "upper(btrim(\"Email\"))",
                        stored: true);

                entity.Property(i => i.Role)
                    .IsRequired()
                    .HasMaxLength(32);

                entity.Property(i => i.Status)
                    .HasConversion<string>()
                    .IsRequired()
                    .HasMaxLength(20);

                entity.HasOne(i => i.Restaurant)
                    .WithMany(r => r.Invitations)
                    .HasForeignKey(i => i.RestaurantId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(i => new
                {
                    i.RestaurantId,
                    i.NormalizedEmail
                })
                .IsUnique()
                .HasFilter("\"Status\" = 'Pending'");

                entity.Property(i => i.TokenHash)
                .HasMaxLength(64);

                 entity.HasIndex(i => i.TokenHash)
                 .IsUnique()
                 .HasFilter("\"TokenHash\" IS NOT NULL");
            });
        }
    }
}
