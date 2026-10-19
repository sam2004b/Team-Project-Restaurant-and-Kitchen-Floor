using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Models;
namespace Restaurant.Api.Data
{
    public class RestaurantDbContext : DbContext
    {
        public RestaurantDbContext(
            DbContextOptions<RestaurantDbContext> options
        ) : base(options)
        {
        }

        public DbSet<Dish> Dishes => Set<Dish>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Dish>(entity =>
            {
                entity.ToTable("dishes");

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
            });
        }
    }
}
