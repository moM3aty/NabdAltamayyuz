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
            else if (User.IsInRole(UserRole.CompanyAdmin.ToString()))
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
            // Real Stats from DB
            ViewBag.CompaniesCount = await _context.Companies.CountAsync();
            ViewBag.EmployeesCount = await _context.Users.CountAsync();
            // Fetch recent companies
            var recentCompanies = await _context.Companies.OrderByDescending(c => c.CreatedAt).Take(5).ToListAsync();

            return View(recentCompanies);
        }

        // 2. Company Admin Dashboard
        [Authorize(Roles = "CompanyAdmin")]
        public async Task<IActionResult> CompanyAdmin()
        {
            // Fetch stats related to the company (assuming multi-tenant logic or simple role filter)
            // For now, fetching all employees as company structure is flat in this demo
            var employeesCount = await _context.Users.CountAsync(u => u.Role == UserRole.Employee);
            ViewBag.EmployeesCount = employeesCount;

            // Get Today's Attendance
            var today = DateTime.Today;
            var attendanceToday = await _context.Attendances
                .Include(a => a.Employee)
                .Where(a => a.Date == today)
                .ToListAsync();

            ViewBag.AttendanceList = attendanceToday;

            // Get Pending Tasks
            var pendingTasks = await _context.WorkTasks
                .Include(t => t.AssignedTo)
                .Where(t => !t.IsCompleted)
                .OrderBy(t => t.DueDate)
                .Take(5)
                .ToListAsync();

            ViewBag.PendingTasks = pendingTasks;

            return View();
        }

        // 3. Employee Dashboard
        [Authorize(Roles = "Employee")]
        public async Task<IActionResult> Employee()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var today = DateTime.Today;

            // Check Attendance Status for Today
            var attendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.EmployeeId == userId && a.Date == today);

            ViewBag.IsCheckedIn = attendance != null && attendance.TimeIn != null;
            ViewBag.IsCheckedOut = attendance != null && attendance.TimeOut != null;

            // Fetch My Active Tasks
            var myTasks = await _context.WorkTasks
                .Where(t => t.AssignedToId == userId && !t.IsCompleted)
                .OrderBy(t => t.DueDate)
                .ToListAsync();

            return View(myTasks);
        }

        // Action: Check In (For Employee)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Employee")]
        public async Task<IActionResult> CheckIn()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var today = DateTime.Today;

            var existingRecord = await _context.Attendances
                .FirstOrDefaultAsync(a => a.EmployeeId == userId && a.Date == today);

            if (existingRecord == null)
            {
                var attendance = new Attendance
                {
                    EmployeeId = userId,
                    Date = today,
                    TimeIn = DateTime.Now,
                    IsManualEntry = false
                };
                _context.Attendances.Add(attendance);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم تسجيل الدخول بنجاح";
            }
            else
            {
                TempData["Warning"] = "لقد قمت بتسجيل الدخول مسبقاً اليوم";
            }

            return RedirectToAction("Employee");
        }

        // Action: Check Out (For Employee)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Employee")]
        public async Task<IActionResult> CheckOut()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var today = DateTime.Today;

            var record = await _context.Attendances
                .FirstOrDefaultAsync(a => a.EmployeeId == userId && a.Date == today);

            if (record != null && record.TimeOut == null)
            {
                record.TimeOut = DateTime.Now;
                _context.Update(record);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم تسجيل الخروج بنجاح";
            }
            else
            {
                TempData["Error"] = "لا يوجد سجل دخول نشط لهذا اليوم";
            }

            return RedirectToAction("Employee");
        }
    }
}