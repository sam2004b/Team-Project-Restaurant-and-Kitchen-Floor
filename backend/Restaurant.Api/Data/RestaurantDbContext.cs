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

        public DbSet<StaffSession> StaffSessions => 
            Set<StaffSession>();

        public DbSet<RestaurantSettings> RestaurantSettings =>
            Set<RestaurantSettings>();

        public DbSet<DiningTable> DiningTables =>
            Set<DiningTable>();

        public DbSet<TableStaffAssignment> TableStaffAssignments =>
            Set<TableStaffAssignment>();

        public DbSet<RestaurantOrder> Orders =>
            Set<RestaurantOrder>();

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

            // S2-US7-T01: revocable, restaurant-scoped Identity sessions.
            modelBuilder.Entity<StaffSession>(entity =>
            {
                entity.ToTable("staff_sessions", table =>
                {
                    table.HasCheckConstraint(
                        "CK_staff_sessions_expiry",
                        "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                });

                entity.HasKey(s => s.Id);
                entity.Property(s => s.UserId).IsRequired();
                entity.Property(s => s.CreatedAtUtc).IsRequired();
                entity.Property(s => s.ExpiresAtUtc).IsRequired();

                entity.HasOne(s => s.User)
                    .WithMany()
                    .HasForeignKey(s => s.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                // A session's restaurant must be the membership's restaurant.
                entity.HasOne(s => s.StaffMembership)
                    .WithMany()
                    .HasForeignKey(s => new { s.RestaurantId, s.StaffMembershipId })
                    .HasPrincipalKey(m => new { m.RestaurantId, m.Id })
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(s => new { s.UserId, s.RevokedAtUtc });
                entity.HasIndex(s => new { s.RestaurantId, s.StaffMembershipId });
            });


            modelBuilder.Entity<RestaurantSettings>(entity =>
            {
                entity.ToTable("restaurant_settings", table =>
                {
                    table.HasCheckConstraint(
                        "CK_restaurant_settings_default_seats_positive",
                        "\"DefaultSeatCapacity\" > 0");
                });

                // One settings record per restaurant.
                entity.HasKey(s => s.RestaurantId);

                entity.Property(s => s.DefaultSeatCapacity)
                    .IsRequired();

                entity.Property(s => s.UpdatedAtUtc)
                    .IsRequired();

                entity.HasOne(s => s.Restaurant)
                    .WithOne()
                    .HasForeignKey<RestaurantSettings>(
                        s => s.RestaurantId)
                    .OnDelete(DeleteBehavior.Cascade);
            });


            modelBuilder.Entity<DiningTable>(entity =>
            {
                entity.ToTable("dining_tables", table =>
                {
                    table.HasCheckConstraint(
                        "CK_dining_tables_number_positive",
                        "\"TableNumber\" > 0");

                    table.HasCheckConstraint(
                        "CK_dining_tables_seats_positive",
                        "\"SeatCount\" > 0");
                });

                entity.HasKey(t => t.Id);

                entity.Property(t => t.TableNumber)
                    .IsRequired();

                entity.Property(t => t.SeatCount)
                    .IsRequired();

                entity.Property(t => t.IsActive)
                    .HasDefaultValue(true);

                entity.HasOne(t => t.Restaurant)
                    .WithMany()
                    .HasForeignKey(t => t.RestaurantId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Table numbers must be unique
                // inside each restaurant.
                entity.HasIndex(t => new
                {
                    t.RestaurantId,
                    t.TableNumber
                }).IsUnique();
            });


            modelBuilder.Entity<TableStaffAssignment>(entity =>
            {
                entity.ToTable("table_staff_assignments");

                entity.HasKey(a => a.Id);

                entity.Property(a => a.AssignedAtUtc)
                    .IsRequired();

                // Each table can have only one current
                // staff assignment.
                entity.HasIndex(a => a.DiningTableId)
                    .IsUnique();

                // The assignment must reference a table
                // belonging to the same restaurant.
                entity.HasOne(a => a.DiningTable)
                    .WithOne(t => t.StaffAssignment)
                    .HasForeignKey<TableStaffAssignment>(
                        a => new
                        {
                            a.RestaurantId,
                            a.DiningTableId
                        })
                    .HasPrincipalKey<DiningTable>(
                        t => new
                        {
                            t.RestaurantId,
                            t.Id
                        })
                    .OnDelete(DeleteBehavior.Cascade);

                // The assigned employee membership must
                // belong to that same restaurant.
                entity.HasOne(a => a.StaffMembership)
                    .WithMany()
                    .HasForeignKey(a => new
                    {
                        a.RestaurantId,
                        a.StaffMembershipId
                    })
                    .HasPrincipalKey(m => new
                    {
                        m.RestaurantId,
                        m.Id
                    })
                    .OnDelete(DeleteBehavior.Restrict);

                // Efficient lookup of an employee's tables.
                entity.HasIndex(a => new
                {
                    a.RestaurantId,
                    a.StaffMembershipId
                });
            });


            modelBuilder.Entity<RestaurantOrder>(entity =>
            {
                entity.HasKey(o => o.Id);

                entity.Property(o => o.CreatedAtUtc)
                    .IsRequired();

                // Older US-8 order headers become Legacy when this migration
                // adds the non-null column. New orders explicitly set Open.
                entity.Property(o => o.Status)
                    .IsRequired()
                    .HasMaxLength(16)
                    .HasDefaultValue(RestaurantOrderStates.Legacy);

                entity.ToTable("orders", table =>
                {
                    table.HasCheckConstraint("CK_orders_state",
                        "\"Status\" IN ('Legacy', 'Open', 'Sent')");
                    table.HasCheckConstraint("CK_orders_sent_consistency",
                        "(\"Status\" = 'Sent' AND \"SentAtUtc\" IS NOT NULL) " +
                        "OR (\"Status\" <> 'Sent' AND \"SentAtUtc\" IS NULL)");
                    table.HasCheckConstraint("CK_orders_sent_after_created",
                        "\"SentAtUtc\" IS NULL OR \"SentAtUtc\" >= \"CreatedAtUtc\"");
                });

                entity.HasIndex(o => new { o.RestaurantId, o.DiningTableId })
                    .IsUnique()
                    .HasDatabaseName("UX_orders_one_open_per_table")
                    .HasFilter("\"Status\" = 'Open'");

                // Order and table must belong
                // to the same restaurant.
                entity.HasOne(o => o.DiningTable)
                    .WithMany()
                    .HasForeignKey(o => new
                    {
                        o.RestaurantId,
                        o.DiningTableId
                    })
                    .HasPrincipalKey(t => new
                    {
                        t.RestaurantId,
                        t.Id
                    })
                    .OnDelete(DeleteBehavior.Restrict);

                // Original waiter membership must
                // belong to the same restaurant.
                entity.HasOne(o => o.CreatedByStaffMembership)
                    .WithMany()
                    .HasForeignKey(o => new
                    {
                        o.RestaurantId,
                        o.CreatedByStaffMembershipId
                    })
                    .HasPrincipalKey(m => new
                    {
                        m.RestaurantId,
                        m.Id
                    })
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(o => new
                {
                    o.RestaurantId,
                    o.DiningTableId,
                    o.CreatedAtUtc
                });
            });

            modelBuilder.Entity<RestaurantOrderItem>(entity =>
            {
                entity.ToTable("order_items", table =>
                {
                    table.HasCheckConstraint("CK_order_items_quantity_positive",
                        "\"Quantity\" > 0");
                    table.HasCheckConstraint("CK_order_items_price_positive",
                        "\"UnitPriceSnapshot\" > 0");
                });

                entity.HasKey(i => i.Id);
                entity.Property(i => i.Quantity).IsRequired();
                entity.Property(i => i.SpecialRequest).HasMaxLength(500);
                entity.Property(i => i.DishNameSnapshot)
                    .IsRequired().HasMaxLength(150);
                entity.Property(i => i.UnitPriceSnapshot).HasPrecision(10, 2);
                entity.Property(i => i.CreatedAtUtc).IsRequired();

                entity.HasOne(i => i.Order)
                    .WithMany(o => o.Items)
                    .HasForeignKey(i => i.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(i => i.Dish)
                    .WithMany()
                    .HasForeignKey(i => i.DishId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(i => i.OrderId);
                entity.HasIndex(i => i.DishId);
            });


        }
    }
}
