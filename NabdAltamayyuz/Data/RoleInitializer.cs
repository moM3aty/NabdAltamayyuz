using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Data;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace NabdAltamayyuz.Models
{
    public static class RoleInitializer
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();

            // 1. إنشاء قاعدة البيانات
            await context.Database.EnsureCreatedAsync();

            // 2. التأكد من وجود شركة افتراضية وحفظها أولاً للحصول على ID
            var mainCompany = await context.Companies.FirstOrDefaultAsync();
            if (mainCompany == null)
            {
                mainCompany = new Company
                {
                    Name = "شركة نبض التميز (تجريبي)",
                    RegistrationNumber = "1010101010",
                    CreatedAt = DateTime.Now
                };
                context.Companies.Add(mainCompany);
                // مهم جداً: الحفظ هنا لتوليد ID للشركة قبل استخدامه مع الموظفين
                await context.SaveChangesAsync();
                Console.WriteLine("--> تم إنشاء الشركة الافتراضية بنجاح.");
            }

            // 3. إنشاء السوبر أدمن (المالك)
            var adminEmail = "admin@nabd.com";
            var adminUser = await context.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);

            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    FullName = "المالك (Super Admin)",
                    Email = adminEmail,
                    PasswordHash = "admin123",
                    Role = UserRole.SuperAdmin,
                    JobTitle = "Owner",
                    PhoneNumber = "0500000000",
                    CreatedAt = DateTime.Now,
                    CompanyId = null // الأدمن غير مرتبط بشركة محددة
                };
                context.Users.Add(adminUser);
                Console.WriteLine("--> تم تجهيز حساب السوبر أدمن.");
            }

            // 4. إنشاء مدير الشركة
            var managerEmail = "manager@company.com";
            if (!await context.Users.AnyAsync(u => u.Email == managerEmail))
            {
                var companyAdmin = new ApplicationUser
                {
                    FullName = "مدير الشركة (Demo)",
                    Email = managerEmail,
                    PasswordHash = "123456",
                    Role = UserRole.CompanyAdmin,
                    JobTitle = "General Manager",
                    PhoneNumber = "0562056821",
                    CreatedAt = DateTime.Now,
                    CompanyId = mainCompany.Id // الآن الـ ID موجود بالتأكيد
                };
                context.Users.Add(companyAdmin);
                Console.WriteLine("--> تم تجهيز حساب مدير الشركة.");
            }

            // 5. إنشاء موظف عادي
            var employeeEmail = "employee@company.com";
            if (!await context.Users.AnyAsync(u => u.Email == employeeEmail))
            {
                var employee = new ApplicationUser
                {
                    FullName = "موظف تجريبي",
                    Email = employeeEmail,
                    PasswordHash = "123456",
                    Role = UserRole.Employee,
                    JobTitle = "HR Specialist",
                    PhoneNumber = "0500000002",
                    CreatedAt = DateTime.Now,
                    CompanyId = mainCompany.Id
                };
                context.Users.Add(employee);
                Console.WriteLine("--> تم تجهيز حساب الموظف.");
            }

            // حفظ جميع المستخدمين دفعة واحدة
            if (context.ChangeTracker.HasChanges())
            {
                await context.SaveChangesAsync();
                Console.WriteLine("--> تم حفظ جميع بيانات المستخدمين في قاعدة البيانات.");
            }
        }
    }
}