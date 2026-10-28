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
        public DbSet<RestaurantOrder> Orders => Set<RestaurantOrder>();
        public DbSet<RestaurantOrderItem> OrderItems =>
            Set<RestaurantOrderItem>();

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

            modelBuilder.Entity<RestaurantOrder>(entity =>
            {
                entity.ToTable("orders");

                entity.HasKey(order => order.Id);

                entity.Property(order => order.CreatedAtUtc)
                    .IsRequired();

                entity.HasMany(order => order.Items)
                    .WithOne(item => item.Order)
                    .HasForeignKey(item => item.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<RestaurantOrderItem>(entity =>
            {
                entity.ToTable("order_items", table =>
                {
                    table.HasCheckConstraint(
                        "CK_order_items_quantity_positive",
                        "\"Quantity\" > 0");

                    table.HasCheckConstraint(
                        "CK_order_items_unit_price_nonnegative",
                        "\"UnitPrice\" >= 0");
                });

                entity.HasKey(item => item.Id);

                entity.Property(item => item.DishName)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.Property(item => item.UnitPrice)
                    .HasPrecision(10, 2);

                entity.HasOne(item => item.Dish)
                    .WithMany()
                    .HasForeignKey(item => item.DishId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(item => item.OrderId);
            });
        }
    }
}
