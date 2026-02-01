using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Data;
using NabdAltamayyuz.Models;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NabdAltamayyuz.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            if (User.IsInRole(UserRole.SuperAdmin.ToString()))
            {
                return RedirectToAction("SuperAdmin");
            }
            // المشرف الرئيسي والفرعي يذهبان لنفس اللوحة
            else if (User.IsInRole(UserRole.CompanyAdmin.ToString()) || User.IsInRole(UserRole.SubAdmin.ToString()))
            {
                return RedirectToAction("CompanyAdmin");
            }
            else
            {
                return RedirectToAction("Employee");
            }
        }

        // 1. Super Admin Dashboard
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> SuperAdmin()
        {
            ViewBag.CompaniesCount = await _context.Companies.CountAsync();
            ViewBag.EmployeesCount = await _context.Users.CountAsync();

            // جلب الشركات مع ترتيب حسب تاريخ الإضافة
            var recentCompanies = await _context.Companies
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return View(recentCompanies);
        }

        // 2. Company Admin & Sub Admin Dashboard
        [Authorize(Roles = "CompanyAdmin,SubAdmin")]
        public async Task<IActionResult> CompanyAdmin()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var user = await _context.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Id == userId);

            if (user?.CompanyId == null) return RedirectToAction("AccessDenied", "Account");

            // إحصائيات الموظفين للشركة
            var employeesCount = await _context.Users.CountAsync(u => u.CompanyId == user.CompanyId && u.Role == UserRole.Employee);
            ViewBag.EmployeesCount = employeesCount;

            // سجل حضور اليوم للشركة
            var today = DateTime.Today;
            var attendanceList = await _context.Attendances
                .Include(a => a.Employee)
                .Where(a => a.Employee.CompanyId == user.CompanyId && a.Date == today)
                .OrderByDescending(a => a.TimeIn)
                .ToListAsync();

            ViewBag.AttendanceList = attendanceList;

            // المهام المعلقة لموظفي الشركة
            var pendingTasks = await _context.WorkTasks
                .Include(t => t.AssignedTo)
                .Where(t => t.AssignedTo.CompanyId == user.CompanyId && !t.IsCompleted)
                .OrderBy(t => t.DueDate)
                .Take(10)
                .ToListAsync();

            ViewBag.PendingTasks = pendingTasks;

            // التحقق من انتهاء الاشتراك
            if (user.Company != null)
            {
                var daysLeft = (user.Company.SubscriptionEndDate - DateTime.Now).Days;
                if (daysLeft < user.Company.NotificationDaysBeforeExpiry && daysLeft > 0)
                {
                    ViewBag.AlertMessage = $"تنبيه: اشتراك الشركة سينتهي خلال {daysLeft} يوم.";
                }
                else if (daysLeft <= 0)
                {
                    ViewBag.ErrorMessage = "تنبيه: اشتراك الشركة منتهي.";
                }
            }

            return View();
        }

        // 3. Employee Dashboard
        [Authorize(Roles = "Employee")]
        public async Task<IActionResult> Employee()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var today = DateTime.Today;

            var attendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.EmployeeId == userId && a.Date == today);

            ViewBag.IsCheckedIn = attendance != null && attendance.TimeIn != null;
            ViewBag.IsCheckedOut = attendance != null && attendance.TimeOut != null;

            // رسالة للموظف في حالة تسجيل الدخول/الخروج
            if (TempData["Success"] != null) ViewBag.Message = TempData["Success"];

            var myTasks = await _context.WorkTasks
                .Where(t => t.AssignedToId == userId && !t.IsCompleted)
                .OrderBy(t => t.DueDate)
                .ToListAsync();

            return View(myTasks);
        }

        // Actions: Check In/Out (Logic remains same, ensuring Authorize is correct)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Employee,CompanyAdmin,SubAdmin")] // Admins might test attendance too
        public async Task<IActionResult> CheckIn()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var today = DateTime.Today;

            var existingRecord = await _context.Attendances.FirstOrDefaultAsync(a => a.EmployeeId == userId && a.Date == today);

            if (existingRecord == null)
            {
                var attendance = new Attendance
                {
                    EmployeeId = userId,
                    Date = today,
                    DayName = today.ToString("dddd", new System.Globalization.CultureInfo("ar-SA")),
                    TimeIn = DateTime.Now,
                    IsManualEntry = false
                };
                _context.Attendances.Add(attendance);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم تسجيل الدخول بنجاح";
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Employee,CompanyAdmin,SubAdmin")]
        public async Task<IActionResult> CheckOut()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var today = DateTime.Today;

            var record = await _context.Attendances.FirstOrDefaultAsync(a => a.EmployeeId == userId && a.Date == today);

            if (record != null && record.TimeOut == null)
            {
                record.TimeOut = DateTime.Now;
                _context.Update(record);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم تسجيل الخروج بنجاح";
            }
            return RedirectToAction("Index");
        }
    }
}