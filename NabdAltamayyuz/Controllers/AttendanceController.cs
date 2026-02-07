using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Data;
using NabdAltamayyuz.Models;
using System;
using System.Collections.Generic;
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

        public AttendanceController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Attendance/MyHistory (للموظف)
        public async Task<IActionResult> MyHistory()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var history = await _context.Attendances
                .Where(a => a.EmployeeId == userId)
                .OrderByDescending(a => a.Date)
                .Take(30)
                .ToListAsync();

            return View(history);
        }

        // GET: Attendance Sheet (للمدراء) - الكشف الشامل
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> Index(int? companyId, DateTime? date, string searchString)
        {
            var selectedDate = date ?? DateTime.Today;
            ViewBag.SelectedDate = selectedDate.ToString("yyyy-MM-dd");
            ViewBag.DayName = selectedDate.ToString("dddd", new CultureInfo("ar-SA"));
            ViewBag.CurrentCompany = companyId;
            ViewBag.CurrentSearch = searchString;

            // 1. تحديد قائمة الموظفين المستهدفين (الجميع، ليس فقط من حضر)
            IQueryable<ApplicationUser> employeesQuery = _context.Users
                .Include(u => u.Company)
                .Where(u => u.Role == UserRole.Employee && !u.IsSuspended);

            // تطبيق الصلاحيات
            if (User.IsInRole("SuperAdmin"))
            {
                // السوبر أدمن: يمكنه اختيار شركة رئيسية أو فرعية
                if (companyId.HasValue)
                {
                    // هل هي شركة رئيسية؟ احضر فروعها أيضاً
                    var subIds = await _context.Companies.Where(c => c.ParentCompanyId == companyId).Select(c => c.Id).ToListAsync();
                    subIds.Add(companyId.Value);
                    employeesQuery = employeesQuery.Where(u => subIds.Contains(u.CompanyId.Value));
                }

                // قائمة الشركات للفلتر
                ViewBag.Companies = new SelectList(await _context.Companies.Where(c => c.ParentCompanyId == null).ToListAsync(), "Id", "Name", companyId);
            }
            else
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);

                if (user?.CompanyId != null)
                {
                    // المشرف: شركته + الفروع
                    var subIds = await _context.Companies.Where(c => c.ParentCompanyId == user.CompanyId).Select(c => c.Id).ToListAsync();
                    subIds.Add(user.CompanyId.Value);

                    if (companyId.HasValue && subIds.Contains(companyId.Value))
                    {
                        employeesQuery = employeesQuery.Where(u => u.CompanyId == companyId.Value);
                    }
                    else
                    {
                        employeesQuery = employeesQuery.Where(u => subIds.Contains(u.CompanyId.Value));
                    }

                    // قائمة الشركات للفلتر (الخاصة بالمشرف)
                    var myCompanies = await _context.Companies
                        .Where(c => subIds.Contains(c.Id))
                        .Select(c => new { c.Id, Name = c.ParentCompanyId == null ? c.Name : " -- " + c.Name })
                        .ToListAsync();
                    ViewBag.Companies = new SelectList(myCompanies, "Id", "Name", companyId);
                }
            }

            // البحث بالاسم
            if (!string.IsNullOrEmpty(searchString))
            {
                employeesQuery = employeesQuery.Where(u => u.FullName.Contains(searchString));
            }

            var employees = await employeesQuery.OrderBy(u => u.FullName).ToListAsync();

            // 2. جلب سجلات الحضور لهذا اليوم لهؤلاء الموظفين
            var empIds = employees.Select(e => e.Id).ToList();
            var attendances = await _context.Attendances
                .Where(a => a.Date == selectedDate && empIds.Contains(a.EmployeeId))
                .ToListAsync();

            // 3. دمج البيانات (Left Join in Memory) لعرض الجميع
            var model = employees.Select(emp => new AttendanceViewModel
            {
                EmployeeId = emp.Id,
                EmployeeName = emp.FullName,
                CompanyName = emp.Company?.Name,
                JobTitle = emp.JobTitle,
                Date = selectedDate,
                // البحث عن سجل الحضور
                AttendanceId = attendances.FirstOrDefault(a => a.EmployeeId == emp.Id)?.Id ?? 0,
                TimeIn = attendances.FirstOrDefault(a => a.EmployeeId == emp.Id)?.TimeIn,
                TimeOut = attendances.FirstOrDefault(a => a.EmployeeId == emp.Id)?.TimeOut,
                Notes = attendances.FirstOrDefault(a => a.EmployeeId == emp.Id)?.Notes,
                IsPresent = attendances.Any(a => a.EmployeeId == emp.Id && a.TimeIn != null)
            }).ToList();

            return View(model);
        }

        // POST: Attendance/ManualEntry (للمدراء - فردي)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> ManualEntry(int employeeId, DateTime date, DateTime? timeIn, DateTime? timeOut, string notes)
        {
            // ملاحظة: DateTime من الفورم يرسل الوقت، نحن نحتاج دمجه مع التاريخ المختار
            // أو الاعتماد على أن الوقت المرسل هو وقت صحيح لليوم

            // تصحيح الوقت ليكون بنفس تاريخ اليوم المختار
            DateTime? finalTimeIn = null;
            DateTime? finalTimeOut = null;

            if (timeIn.HasValue)
                finalTimeIn = date.Date.Add(timeIn.Value.TimeOfDay);

            if (timeOut.HasValue)
                finalTimeOut = date.Date.Add(timeOut.Value.TimeOfDay);

            var existingRecord = await _context.Attendances
                .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Date == date.Date);

            if (existingRecord != null)
            {
                existingRecord.TimeIn = finalTimeIn;
                existingRecord.TimeOut = finalTimeOut;
                existingRecord.Notes = notes;
                existingRecord.IsManualEntry = true;
                _context.Update(existingRecord);
            }
            else
            {
                if (finalTimeIn != null || finalTimeOut != null) // لا نحفظ سجل فارغ
                {
                    var attendance = new Attendance
                    {
                        EmployeeId = employeeId,
                        Date = date.Date,
                        DayName = date.ToString("dddd", new CultureInfo("ar-SA")),
                        TimeIn = finalTimeIn,
                        TimeOut = finalTimeOut,
                        Notes = notes,
                        IsManualEntry = true
                    };
                    _context.Add(attendance);
                }
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "تم حفظ السجل";
            return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
        }

        // POST: Attendance/Edit (تعديل سريع من الجدول)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> Edit(int id, DateTime? TimeIn, DateTime? TimeOut, string Notes)
        {
            var attendance = await _context.Attendances.FindAsync(id);
            if (attendance == null) return NotFound();

            // دمج الوقت الجديد مع تاريخ السجل الأصلي
            if (TimeIn.HasValue) attendance.TimeIn = attendance.Date.Date.Add(TimeIn.Value.TimeOfDay);
            if (TimeOut.HasValue) attendance.TimeOut = attendance.Date.Date.Add(TimeOut.Value.TimeOfDay);

            if (Notes != null) attendance.Notes = Notes;

            attendance.IsManualEntry = true;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { date = attendance.Date.ToString("yyyy-MM-dd") });
        }

        // Helper ViewModel for the Sheet
        public class AttendanceViewModel
        {
            public int EmployeeId { get; set; }
            public string EmployeeName { get; set; }
            public string CompanyName { get; set; }
            public string JobTitle { get; set; }
            public int AttendanceId { get; set; } // 0 if no record
            public DateTime Date { get; set; }
            public DateTime? TimeIn { get; set; }
            public DateTime? TimeOut { get; set; }
            public string Notes { get; set; }
            public bool IsPresent { get; set; }
        }
    }
}