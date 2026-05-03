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
using System.Collections.Generic;

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
        // 1. سجل حضوري (للموظف) - (التعديل: إضافة اسم الشركة للطباعة)
        // ---------------------------------------------------------
        public async Task<IActionResult> MyHistory()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var user = await _context.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Id == userId);
            // إرسال اسم الشركة للواجهة ليظهر في الترويسة المطبوعة
            ViewBag.CompanyName = user?.Company?.Name ?? "نبض التميز الذهبي";

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
            // استخدام توقيت السعودية كافتراضي إذا لم يتم اختيار تاريخ
            var selectedDate = date ?? DateTime.UtcNow.AddHours(3).Date;

            var usersQuery = _context.Users.Include(u => u.Company).Where(u => u.Role == UserRole.Employee);

            if (User.IsInRole("SuperAdmin"))
            {
                if (companyId.HasValue) usersQuery = usersQuery.Where(u => u.CompanyId == companyId);
                ViewBag.Companies = new SelectList(await _context.Companies.ToListAsync(), "Id", "Name", companyId);
            }
            else
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);

                if (user?.CompanyId != null)
                {
                    usersQuery = usersQuery.Where(u => u.CompanyId == user.CompanyId);
                }
            }

            if (!string.IsNullOrEmpty(searchEmployee))
            {
                usersQuery = usersQuery.Where(u => u.FullName.Contains(searchEmployee));
            }

            if (!string.IsNullOrEmpty(status))
            {
                usersQuery = usersQuery.Where(u => u.Status == status);
            }

            var employees = await usersQuery.OrderBy(u => u.FullName).ToListAsync();

            var empIds = employees.Select(e => e.Id).ToList();
            var attendances = await _context.Attendances
                .Where(a => a.Date == selectedDate && empIds.Contains(a.EmployeeId))
                .ToListAsync();

            var resultList = new List<Attendance>();
            foreach (var emp in employees)
            {
                var record = attendances.FirstOrDefault(a => a.EmployeeId == emp.Id);
                if (record != null)
                {
                    record.Employee = emp;
                    resultList.Add(record);
                }
                else
                {
                    resultList.Add(new Attendance
                    {
                        Id = 0,
                        EmployeeId = emp.Id,
                        Employee = emp,
                        Date = selectedDate,
                        DayName = selectedDate.ToString("dddd", new CultureInfo("ar-SA")),
                        TimeIn = null,
                        TimeOut = null
                    });
                }
            }

            ViewBag.SelectedDate = selectedDate.ToString("yyyy-MM-dd");
            ViewBag.DayName = selectedDate.ToString("dddd", new CultureInfo("ar-SA"));
            ViewBag.CurrentSearch = searchEmployee;
            ViewBag.CurrentStatus = status;
            ViewBag.CurrentCompany = companyId;

            var sortedList = resultList
                .OrderByDescending(a => a.TimeIn.HasValue)
                .ThenBy(a => a.Employee.FullName)
                .ToList();

            return View(sortedList);
        }

        // ---------------------------------------------------------
        // إجراء سريع: تسجيل الدخول / الانصراف من الجدول مباشرة
        // ---------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> QuickAction(int employeeId, DateTime date, string actionType)
        {
            var employee = await _context.Users.FindAsync(employeeId);
            if (employee == null) return Json(new { success = false, message = "الموظف غير موجود" });

            if (!User.IsInRole("SuperAdmin"))
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(currentUserId);
                if (employee.CompanyId != currentUser.CompanyId) return Json(new { success = false, message = "لا تملك صلاحية" });
            }

            var record = await _context.Attendances.FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Date == date.Date);

            // حل مشكلة تأخير الوقت (استخدام توقيت السعودية الثابت UTC+3)
            var nowTime = DateTime.UtcNow.AddHours(3);
            var actionDateTime = new DateTime(date.Year, date.Month, date.Day, nowTime.Hour, nowTime.Minute, nowTime.Second);

            if (actionType == "In")
            {
                if (record != null && record.TimeIn != null) return Json(new { success = false, message = "تم تسجيل الدخول مسبقاً" });

                if (record == null)
                {
                    record = new Attendance
                    {
                        EmployeeId = employeeId,
                        Date = date.Date,
                        DayName = date.ToString("dddd", new CultureInfo("ar-SA")),
                        TimeIn = actionDateTime,
                        IsManualEntry = true
                    };
                    _context.Attendances.Add(record);
                }
                else
                {
                    record.TimeIn = actionDateTime;
                    record.IsManualEntry = true;
                    _context.Update(record);
                }

                await _context.SaveChangesAsync();
                if (!string.IsNullOrEmpty(employee.NationalId)) await _teleworksService.SendAttendanceAsync(employee.NationalId, date.Date, actionDateTime, null);
            }
            else if (actionType == "Out")
            {
                if (record == null || record.TimeIn == null) return Json(new { success = false, message = "يجب تسجيل الدخول أولاً" });
                if (record.TimeOut != null) return Json(new { success = false, message = "تم تسجيل الانصراف مسبقاً" });

                record.TimeOut = actionDateTime;
                record.IsManualEntry = true;
                _context.Update(record);
                await _context.SaveChangesAsync();

                if (!string.IsNullOrEmpty(employee.NationalId)) await _teleworksService.SendAttendanceAsync(employee.NationalId, date.Date, record.TimeIn.Value, actionDateTime);
            }

            return Json(new { success = true, message = "تم تسجيل الحركة بنجاح" });
        }


        // ---------------------------------------------------------
        // 3. التسجيل والتعديل اليدوي من النافذة المنبثقة
        // ---------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> ManualEntry(int employeeId, DateTime date, DateTime? timeIn, DateTime? timeOut, string notes)
        {
            var employee = await _context.Users.FindAsync(employeeId);
            if (employee == null) return NotFound();

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
                existingRecord.TimeIn = timeIn;
                existingRecord.TimeOut = timeOut;

                // التعديل 3: إزالة الإضافة الإجبارية لكلمة "تعديل يدوي" للحفاظ على الملاحظات نظيفة
                existingRecord.Notes = notes;
                existingRecord.IsManualEntry = true;
                _context.Update(existingRecord);
            }
            else if (timeIn.HasValue || timeOut.HasValue || !string.IsNullOrEmpty(notes))
            {
                var attendance = new Attendance
                {
                    EmployeeId = employeeId,
                    Date = date.Date,
                    DayName = date.ToString("dddd", new CultureInfo("ar-SA")),
                    TimeIn = timeIn,
                    TimeOut = timeOut,
                    Notes = notes, // يحفظ الملاحظات المكتوبة فقط
                    IsManualEntry = true
                };
                _context.Add(attendance);
            }

            await _context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(employee.NationalId) && timeIn.HasValue)
            {
                await _teleworksService.SendAttendanceAsync(employee.NationalId, date, timeIn.Value, timeOut);
            }

            TempData["Success"] = "تم حفظ التعديلات بنجاح";
            return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
        }

        // ---------------------------------------------------------
        // 5. AJAX: جلب الموظفين لشركة محددة (للسوبر أدمن والتقارير)
        // ---------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetEmployeesByCompany(int companyId)
        {
            if (!User.IsInRole("SuperAdmin"))
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(currentUserId);
                if (companyId != currentUser.CompanyId) return Forbid();
            }

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
            var endDate = DateTime.UtcNow.AddHours(3).Date;
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
    }
}