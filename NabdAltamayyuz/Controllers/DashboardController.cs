using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Data;
using NabdAltamayyuz.Models;
using NabdAltamayyuz.Services;
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
        private readonly ITeleworksService _teleworksService;

        public DashboardController(ApplicationDbContext context, ITeleworksService teleworksService)
        {
            _context = context;
            _teleworksService = teleworksService;
        }

        public IActionResult Index()
        {
            if (User.IsInRole("SuperAdmin")) return RedirectToAction("SuperAdmin");
            else if (User.IsInRole("CompanyAdmin") || User.IsInRole("SubAdmin")) return RedirectToAction("CompanyAdmin");
            else return RedirectToAction("Employee");
        }

        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> SuperAdmin()
        {
            ViewBag.CompaniesCount = await _context.Companies.CountAsync();
            ViewBag.EmployeesCount = await _context.Users.CountAsync(u => u.Role == UserRole.Employee);

            var allTasks = await _context.WorkTasks.ToListAsync();
            ViewBag.TasksTotal = allTasks.Count;
            ViewBag.TasksCompleted = allTasks.Count(t => t.Status == Models.TaskStatus.Completed);
            ViewBag.TasksPending = allTasks.Count(t => t.Status == Models.TaskStatus.Pending);
            ViewBag.TasksDelayed = allTasks.Count(t => t.Status == Models.TaskStatus.Delayed);
            ViewBag.TasksOverdue = allTasks.Count(t => t.DueDate < DateTime.Today && t.Status != Models.TaskStatus.Completed);

            var recentCompanies = await _context.Companies.OrderByDescending(c => c.CreatedAt).Take(5).ToListAsync();
            return View(recentCompanies);
        }

        [Authorize(Roles = "CompanyAdmin,SubAdmin")]
        public async Task<IActionResult> CompanyAdmin()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var user = await _context.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.CompanyId == null) return RedirectToAction("AccessDenied", "Account");

            ViewBag.EmployeesCount = await _context.Users.CountAsync(u => u.CompanyId == user.CompanyId && u.Role == UserRole.Employee);

            // استخدام توقيت السعودية
            var today = DateTime.UtcNow.AddHours(3).Date;
            ViewBag.AttendanceList = await _context.Attendances.Include(a => a.Employee).Where(a => a.Employee.CompanyId == user.CompanyId && a.Date == today).OrderByDescending(a => a.TimeIn).ToListAsync();
            ViewBag.PendingTasks = await _context.WorkTasks.Include(t => t.AssignedTo).Where(t => t.AssignedTo.CompanyId == user.CompanyId && !t.IsCompleted).OrderBy(t => t.DueDate).Take(10).ToListAsync();

            if (user.Company != null)
            {
                // استخدام توقيت السعودية
                var daysLeft = (user.Company.SubscriptionEndDate - DateTime.UtcNow.AddHours(3)).Days;
                if (daysLeft < user.Company.NotificationDaysBeforeExpiry && daysLeft > 0) ViewBag.AlertMessage = $"تنبيه: الاشتراك ينتهي خلال {daysLeft} يوم.";
                else if (daysLeft <= 0) ViewBag.ErrorMessage = "تنبيه: الاشتراك منتهي.";
            }
            return View();
        }

        [Authorize(Roles = "Employee")]
        public async Task<IActionResult> Employee()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            // استخدام توقيت السعودية
            var today = DateTime.UtcNow.AddHours(3).Date;
            var attendance = await _context.Attendances.FirstOrDefaultAsync(a => a.EmployeeId == userId && a.Date == today);

            ViewBag.IsCheckedIn = attendance != null && attendance.TimeIn != null;
            ViewBag.IsCheckedOut = attendance != null && attendance.TimeOut != null;

            if (TempData["Success"] != null) ViewBag.Message = TempData["Success"];

            var myTasks = await _context.WorkTasks.Where(t => t.AssignedToId == userId && !t.IsCompleted).OrderBy(t => t.DueDate).ToListAsync();

            var allMyTasks = await _context.WorkTasks.Where(t => t.AssignedToId == userId).ToListAsync();
            ViewBag.MyTotalTasks = allMyTasks.Count;
            ViewBag.MyCompletedTasks = allMyTasks.Count(t => t.IsCompleted);
            ViewBag.MyPendingTasks = allMyTasks.Count(t => !t.IsCompleted);

            return View(myTasks);
        }

        // --- تسجيل الدخول (مع الربط بالمنصة) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Employee,CompanyAdmin,SubAdmin")]
        public async Task<IActionResult> CheckIn()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            // ضبط الوقت لتوقيت السعودية (UTC+3)
            var nowTime = DateTime.UtcNow.AddHours(3);
            var today = nowTime.Date;

            var existingRecord = await _context.Attendances.FirstOrDefaultAsync(a => a.EmployeeId == userId && a.Date == today);

            if (existingRecord == null)
            {
                var attendance = new Attendance
                {
                    EmployeeId = userId,
                    Date = today,
                    DayName = today.ToString("dddd", new System.Globalization.CultureInfo("ar-SA")),
                    TimeIn = nowTime,
                    IsManualEntry = false
                };
                _context.Attendances.Add(attendance);
                await _context.SaveChangesAsync();

                // إرسال البيانات للمنصة
                var user = await _context.Users.FindAsync(userId);
                if (user != null && !string.IsNullOrEmpty(user.NationalId))
                {
                    // إرسال في الخلفية
                    await _teleworksService.SendAttendanceAsync(user.NationalId, today, nowTime, null);
                }

                TempData["Success"] = "تم تسجيل الدخول";
            }
            return RedirectToAction("Index");
        }

        // --- تسجيل الخروج (مع الربط بالمنصة) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Employee,CompanyAdmin,SubAdmin")]
        public async Task<IActionResult> CheckOut()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            // ضبط الوقت لتوقيت السعودية (UTC+3)
            var nowTime = DateTime.UtcNow.AddHours(3);
            var today = nowTime.Date;

            var record = await _context.Attendances.FirstOrDefaultAsync(a => a.EmployeeId == userId && a.Date == today);

            if (record != null && record.TimeOut == null)
            {
                record.TimeOut = nowTime;
                _context.Update(record);
                await _context.SaveChangesAsync();

                // إرسال البيانات للمنصة
                var user = await _context.Users.FindAsync(userId);
                if (user != null && !string.IsNullOrEmpty(user.NationalId))
                {
                    await _teleworksService.SendAttendanceAsync(user.NationalId, today, record.TimeIn.Value, nowTime);
                }

                TempData["Success"] = "تم تسجيل الخروج";
            }
            return RedirectToAction("Index");
        }
    }
}