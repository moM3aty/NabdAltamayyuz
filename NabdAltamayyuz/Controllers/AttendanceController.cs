using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Data;
using NabdAltamayyuz.Models;
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
                .Take(30) // آخر 30 يوم
                .ToListAsync();

            return View(history);
        }

        // GET: Attendance Sheet (للمدراء)
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> Index(int? companyId, string searchEmployee, DateTime? date, string status)
        {
            var query = _context.Attendances
                .Include(a => a.Employee)
                .ThenInclude(e => e.Company)
                .AsQueryable();

            // 1. تصفية حسب الصلاحية وتجهيز القوائم
            if (User.IsInRole("SuperAdmin"))
            {
                if (companyId.HasValue) query = query.Where(a => a.Employee.CompanyId == companyId);

                // قائمة الشركات للبحث
                ViewBag.Companies = new SelectList(await _context.Companies.ToListAsync(), "Id", "Name", companyId);
            }
            else
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);

                if (user?.CompanyId != null)
                {
                    query = query.Where(a => a.Employee.CompanyId == user.CompanyId);

                    // تجهيز قائمة موظفي الشركة لنموذج الإضافة اليدوية
                    var employees = await _context.Users
                        .Where(u => u.CompanyId == user.CompanyId && u.Role == UserRole.Employee)
                        .Select(u => new { Id = u.Id, Name = u.FullName })
                        .OrderBy(x => x.Name)
                        .ToListAsync();
                    ViewBag.Employees = new SelectList(employees, "Id", "Name");
                }
            }

            // 2. فلتر التاريخ (الافتراضي: اليوم)
            var selectedDate = date ?? DateTime.Today;
            query = query.Where(a => a.Date == selectedDate);

            // بيانات العرض
            ViewBag.SelectedDate = selectedDate.ToString("yyyy-MM-dd");
            ViewBag.DayName = selectedDate.ToString("dddd", new CultureInfo("ar-SA"));

            // 3. فلتر البحث بالاسم
            if (!string.IsNullOrEmpty(searchEmployee))
            {
                query = query.Where(a => a.Employee.FullName.Contains(searchEmployee));
            }

            // 4. فلتر الحالة (نشط - للموظف)
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(a => a.Employee.Status == status);
            }

            // حفظ قيم البحث
            ViewBag.CurrentSearch = searchEmployee;
            ViewBag.CurrentStatus = status;
            ViewBag.CurrentCompany = companyId;

            var data = await query.OrderByDescending(a => a.TimeIn).ToListAsync();
            return View(data);
        }

        // POST: Attendance/ManualEntry (للمدراء)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> ManualEntry(int employeeId, DateTime date, DateTime timeIn, DateTime? timeOut, string notes)
        {
            // التحقق من وجود الموظف وصلاحية الوصول إليه
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
                // تعديل سجل موجود
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
            TempData["Success"] = "تم حفظ سجل الحضور";

            // العودة لنفس يوم التاريخ المدخل
            return RedirectToAction(nameof(Index), new { date = date.ToString("yyyy-MM-dd") });
        }

        // GET: Attendance/Edit/5
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var attendance = await _context.Attendances
                .Include(a => a.Employee)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (attendance == null) return NotFound();

            // التحقق من الصلاحية
            if (!User.IsInRole("SuperAdmin"))
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);
                if (attendance.Employee.CompanyId != user.CompanyId) return Forbid();
            }

            return View(attendance);
        }

        // POST: Attendance/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> Edit(int id, Attendance model)
        {
            if (id != model.Id) return NotFound();

            var attendance = await _context.Attendances.FindAsync(id);
            if (attendance == null) return NotFound();

            // تحديث الحقول المسموح بها فقط
            attendance.TimeIn = model.TimeIn;
            attendance.TimeOut = model.TimeOut;
            attendance.Notes = model.Notes;
            attendance.IsManualEntry = true; // تم التعديل يدوياً

            _context.Update(attendance);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم تحديث السجل بنجاح";
            return RedirectToAction(nameof(Index), new { date = attendance.Date.ToString("yyyy-MM-dd") });
        }

        // AJAX Helper: جلب موظفي شركة معينة (للسوبر أدمن في المودال)
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
    }
}