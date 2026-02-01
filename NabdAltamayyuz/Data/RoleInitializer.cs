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

            // 2. الشركة الافتراضية (تحديث لتشمل بيانات الاشتراك)
            var mainCompany = await context.Companies.FirstOrDefaultAsync();
            if (mainCompany == null)
            {
                mainCompany = new Company
                {
                    Name = "شركة نبض التميز (تجريبي)",
                    UnifiedNumber = "7001234567",
                    RegistrationNumber = "1010101010",
                    TaxNumber = "300123456700003",
                    Email = "info@nabd.com",
                    PhoneNumber = "0562056821",
                    ResponsiblePerson = "المدير العام",
                    NationalAddressShortCode = "RRRD2929",

                    // بيانات الاشتراك (مهمة لظهور لوحة التحكم بشكل صحيح)
                    SubscriptionStartDate = DateTime.Now,
                    SubscriptionEndDate = DateTime.Now.AddYears(1), // اشتراك لمدة سنة
                    PaymentTerm = PaymentTerm.Annual,
                    NotificationDaysBeforeExpiry = 30,

                    // الحدود
                    AllowedEmployees = 50,
                    AllowedSubAccounts = 5,

                    // المالية
                    PricePerEmployee = 100,
                    TaxRate = 15,

                    CreatedAt = DateTime.Now,
                    IsSuspended = false
                };

                mainCompany.CalculateTotal(); // حساب الإجمالي

                context.Companies.Add(mainCompany);
                await context.SaveChangesAsync();
                Console.WriteLine("--> تم إنشاء الشركة الافتراضية مع بيانات الاشتراك.");
            }

            // 3. السوبر أدمن
            var adminEmail = "admin@nabd.com";
            if (!await context.Users.AnyAsync(u => u.Email == adminEmail))
            {
                var adminUser = new ApplicationUser
                {
                    FullName = "المالك (Super Admin)",
                    Email = adminEmail,
                    PasswordHash = "admin123",
                    Role = UserRole.SuperAdmin,
                    JobTitle = "Owner",
                    PhoneNumber = "0500000000",
                    CreatedAt = DateTime.Now,
                    Status = "Active"
                };
                context.Users.Add(adminUser);
            }

            // 4. مدير الشركة
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
                    NationalId = "1010101010",
                    CreatedAt = DateTime.Now,
                    CompanyId = mainCompany.Id,
                    Status = "Active"
                };
                context.Users.Add(companyAdmin);
            }

            // 5. موظف عادي
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
                    NationalId = "1020202020",
                    CreatedAt = DateTime.Now,
                    CompanyId = mainCompany.Id,
                    Status = "Active"
                };
                context.Users.Add(employee);
            }

            if (context.ChangeTracker.HasChanges())
            {
                await context.SaveChangesAsync();
            }
        }
    }
}