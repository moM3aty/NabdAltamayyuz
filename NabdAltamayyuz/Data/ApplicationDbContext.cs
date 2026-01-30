using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Models;

namespace NabdAltamayyuz.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // DB Sets (Tables)
        public DbSet<Company> Companies { get; set; }
        public DbSet<ApplicationUser> Users { get; set; }
        public DbSet<Attendance> Attendances { get; set; }
        public DbSet<WorkTask> WorkTasks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Relationships

            // Company -> Employees
            modelBuilder.Entity<ApplicationUser>()
                .HasOne(u => u.Company)
                .WithMany(c => c.Employees)
                .HasForeignKey(u => u.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Task -> AssignedTo (Employee)
            modelBuilder.Entity<WorkTask>()
                .HasOne(t => t.AssignedTo)
                .WithMany()
                .HasForeignKey(t => t.AssignedToId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete

            // Task -> CreatedBy (Manager)
            modelBuilder.Entity<WorkTask>()
                .HasOne(t => t.CreatedBy)
                .WithMany()
                .HasForeignKey(t => t.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}