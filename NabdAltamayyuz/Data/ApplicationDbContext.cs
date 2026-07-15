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

        // الجداول الجديدة المضافة
        public DbSet<Project> Projects { get; set; }
        public DbSet<ProjectJobRole> ProjectJobRoles { get; set; }
        public DbSet<LeaveRequest> LeaveRequests { get; set; }
        public DbSet<MonthlyInteraction> MonthlyInteractions { get; set; }

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
                .OnDelete(DeleteBehavior.Restrict);

            // Task -> CreatedBy (Manager)
            modelBuilder.Entity<WorkTask>()
                .HasOne(t => t.CreatedBy)
                .WithMany()
                .HasForeignKey(t => t.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            // Company -> Projects
            modelBuilder.Entity<Project>()
                .HasOne(p => p.Company)
                .WithMany(c => c.Projects)
                .HasForeignKey(p => p.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            // Project -> JobRoles
            modelBuilder.Entity<ProjectJobRole>()
                .HasOne(r => r.Project)
                .WithMany(p => p.JobRoles)
                .HasForeignKey(r => r.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // LeaveRequest -> Employee
            modelBuilder.Entity<LeaveRequest>()
                .HasOne(l => l.Employee)
                .WithMany()
                .HasForeignKey(l => l.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);

            // MonthlyInteraction -> Employee
            modelBuilder.Entity<MonthlyInteraction>()
                .HasOne(m => m.Employee)
                .WithMany()
                .HasForeignKey(m => m.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}