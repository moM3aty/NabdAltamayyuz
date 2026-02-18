using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Data;
using NabdAltamayyuz.Models;
using NabdAltamayyuz.Services;
using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NabdAltamayyuz.Controllers
{
    [Authorize]
    public class AttendanceController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ITeleworksService _teleworksService;

        public AttendanceController(ApplicationDbContext context, ITeleworksService teleworksService)
        {
            _context = context;
            _teleworksService = teleworksService;
        }

        // ---------------------------------------------------------
        // 1. سجل حضوري (للموظف)
        // ---------------------------------------------------------
        public async Task<IActionResult> MyHistory()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var history = await _context.Attendances
                .Where(a => a.EmployeeId == userId)
                .OrderByDescending(a => a.Date)
                .Take(30) // آخر 30 يوم
                .ToListAsync();

            return View(history);
        }

        // ---------------------------------------------------------
        // 2. سجل الحضور العام (للمدراء والمالك)
        // ---------------------------------------------------------
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> Index(int? companyId, string searchEmployee, DateTime? date, string status)
        {
            var query = _context.Attendances
                .Include(a => a.Employee)
                .ThenInclude(e => e.Company)
                .AsQueryable();

            // أ. تحديد الصلاحيات وتجهيز القوائم
            if (User.IsInRole("SuperAdmin"))
            {
                if (companyId.HasValue) query = query.Where(a => a.Employee.CompanyId == companyId);

                ViewBag.Companies = new SelectList(await _context.Companies.ToListAsync(), "Id", "Name", companyId);

                // إذا تم اختيار شركة في الفلتر، نملأ قائمة الموظفين
                if (companyId.HasValue)
                {
                    var employees = await _context.Users
                        .Where(u => u.CompanyId == companyId && u.Role == UserRole.Employee)
                        .Select(u => new { Id = u.Id, Name = u.FullName })
                        .OrderBy(x => x.Name)
                        .ToListAsync();
                    ViewBag.Employees = new SelectList(employees, "Id", "Name");
                }
            }
            else
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);

                if (user?.CompanyId != null)
                {
                    query = query.Where(a => a.Employee.CompanyId == user.CompanyId);

                    var employees = await _context.Users
                        .Where(u => u.CompanyId == user.CompanyId && u.Role == UserRole.Employee)
                        .Select(u => new { Id = u.Id, Name = u.FullName })
                        .OrderBy(x => x.Name)
                        .ToListAsync();
                    ViewBag.Employees = new SelectList(employees, "Id", "Name");
                }
            }

            // ب. فلتر التاريخ (الافتراضي: اليوم)
            var selectedDate = date ?? DateTime.Today;
            query = query.Where(a => a.Date == selectedDate);

            ViewBag.SelectedDate = selectedDate.ToString("yyyy-MM-dd");
            ViewBag.DayName = selectedDate.ToString("dddd", new CultureInfo("ar-SA"));

            // ج. فلتر البحث بالاسم
            if (!string.IsNullOrEmpty(searchEmployee))
            {
                query = query.Where(a => a.Employee.FullName.Contains(searchEmployee));
            }

            // د. فلتر الحالة (نشط/غير نشط للموظف)
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(a => a.Employee.Status == status);
            }

            ViewBag.CurrentSearch = searchEmployee;
            ViewBag.CurrentStatus = status;
            ViewBag.CurrentCompany = companyId;

            var data = await query.OrderByDescending(a => a.TimeIn).ToListAsync();
            return View(data);
        }

        // ---------------------------------------------------------
        // 3. التسجيل اليدوي (للمدراء)
        // ---------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> ManualEntry(int employeeId, DateTime date, DateTime timeIn, DateTime? timeOut, string notes)
        {
            var employee = await _context.Users.FindAsync(employeeId);
            if (employee == null) return NotFound();

            // التحقق من الصلاحية (للمشرفين فقط)
            if (!User.IsInRole("SuperAdmin"))
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(currentUserId);
                if (employee.CompanyId != currentUser.CompanyId) return Forbid();
            }

            var existingRecord = await _context.Attendances
                .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Date == date.Date);

            if (existingRecord != null)
            {
                // تحديث سجل موجود
                existingRecord.TimeIn = timeIn;
                existingRecord.TimeOut = timeOut;
                existingRecord.Notes = notes + " (تعديل يدوي)";
                existingRecord.IsManualEntry = true;
                _context.Update(existingRecord);
            }
            else
            {
                // إضافة سجل جديد
                var attendance = new Attendance
                {
                    EmployeeId = employeeId,
                    Date = date.Date,
                    DayName = date.ToString("dddd", new CultureInfo("ar-SA")),
                    TimeIn = timeIn,
                    TimeOut = timeOut,
                    Notes = notes,
                    IsManualEntry = true
                };
                _context.Add(attendance);
            }

            await _context.SaveChangesAsync();

            // إرسال البيانات للمنصة (Teleworks API)
            if (!string.IsNullOrEmpty(employee.NationalId))
            {
                await _teleworksService.SendAttendanceAsync(employee.NationalId, date, timeIn, timeOut);
            }

            TempData["Success"] = "تم حفظ سجل الحضور";
            return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
        }

        // ---------------------------------------------------------
        // 4. تعديل سجل (Edit)
        // ---------------------------------------------------------
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var attendance = await _context.Attendances
                .Include(a => a.Employee)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (attendance == null) return NotFound();

            if (!User.IsInRole("SuperAdmin"))
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);
                if (attendance.Employee.CompanyId != user.CompanyId) return Forbid();
            }

            return View(attendance); // تحتاج لإنشاء View بسيط (Edit.cshtml) لهذا الغرض أو استخدام المودال في Index
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> Edit(int id, Attendance model)
        {
            if (id != model.Id) return NotFound();

            var attendance = await _context.Attendances
                .Include(a => a.Employee)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (attendance == null) return NotFound();

            // تحديث الحقول
            attendance.TimeIn = model.TimeIn;
            attendance.TimeOut = model.TimeOut;
            attendance.Notes = model.Notes;
            attendance.IsManualEntry = true;

            _context.Update(attendance);
            await _context.SaveChangesAsync();

            // تحديث المنصة
            if (attendance.Employee != null && !string.IsNullOrEmpty(attendance.Employee.NationalId) && attendance.TimeIn.HasValue)
            {
                await _teleworksService.SendAttendanceAsync(attendance.Employee.NationalId, attendance.Date, attendance.TimeIn.Value, attendance.TimeOut);
            }

            TempData["Success"] = "تم تحديث السجل بنجاح";
            return RedirectToAction(nameof(Index), new { date = attendance.Date.ToString("yyyy-MM-dd") });
        }

        // ---------------------------------------------------------
        // 5. AJAX: جلب الموظفين لشركة محددة (للسوبر أدمن)
        // ---------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetEmployeesByCompany(int companyId)
        {
            if (!User.IsInRole("SuperAdmin")) return Forbid();

            var employees = await _context.Users
                .Where(u => u.CompanyId == companyId && u.Role == UserRole.Employee)
                .OrderBy(u => u.FullName)
                .Select(u => new { id = u.Id, name = u.FullName })
                .ToListAsync();

            return Json(employees);
        }

        // ---------------------------------------------------------
        // 6. إرسال البيانات المجمع (أسبوعي)
        // ---------------------------------------------------------
        [HttpPost]
        [Authorize(Roles = "CompanyAdmin,SuperAdmin")]
        public async Task<IActionResult> SubmitWeeklyData(int? companyId)
        {
            // الأسبوع الماضي
            var endDate = DateTime.Today;
            var startDate = endDate.AddDays(-6);

            var query = _context.Attendances
                .Include(a => a.Employee)
                .Where(a => a.Date >= startDate && a.Date <= endDate);

            if (!User.IsInRole("SuperAdmin"))
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);
                query = query.Where(a => a.Employee.CompanyId == user.CompanyId);
            }
            else if (companyId.HasValue)
            {
                query = query.Where(a => a.Employee.CompanyId == companyId);
            }

            var logs = await query.ToListAsync();
            int sentCount = 0;

            foreach (var log in logs)
            {
                if (!string.IsNullOrEmpty(log.Employee.NationalId) && log.TimeIn.HasValue)
                {
                    var success = await _teleworksService.SendAttendanceAsync(log.Employee.NationalId, log.Date, log.TimeIn.Value, log.TimeOut);
                    if (success) sentCount++;
                }
            }

            return Json(new { success = true, message = $"تم إرسال {sentCount} سجل حضور للمنصة عن الأسبوع الماضي." });
        }
    

        public class AttendanceViewModel
        {
            public int EmployeeId { get; set; }
            public string EmployeeName { get; set; }
            public string CompanyName { get; set; }
            public string JobTitle { get; set; }
            public int AttendanceId { get; set; } 
            public DateTime Date { get; set; }
            public DateTime? TimeIn { get; set; }
            public DateTime? TimeOut { get; set; }
            public string Notes { get; set; }
            public bool IsPresent { get; set; }
        }
    }
}