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
        public async Task<IActionResult> Index(string searchEmployee, DateTime? startDate, DateTime? endDate)
        {
            var query = _context.Attendances
                .Include(a => a.Employee)
                .AsQueryable();

            if (!User.IsInRole("SuperAdmin"))
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);
                query = query.Where(a => a.Employee.CompanyId == user.CompanyId);
            }

            if (!string.IsNullOrEmpty(searchEmployee))
                query = query.Where(a => a.Employee.FullName.Contains(searchEmployee));

            if (startDate.HasValue) query = query.Where(a => a.Date >= startDate.Value);
            if (endDate.HasValue) query = query.Where(a => a.Date <= endDate.Value);

            var data = await query.OrderByDescending(a => a.Date).ToListAsync();
            return View(data);
        }

        // POST: Attendance/ManualEntry (للمدراء)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> ManualEntry(int employeeId, DateTime date, DateTime timeIn, DateTime? timeOut, string notes)
        {


            var existingRecord = await _context.Attendances
                .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Date == date.Date);

            if (existingRecord != null)
            {
                existingRecord.TimeIn = timeIn;
                existingRecord.TimeOut = timeOut;
                existingRecord.Notes = notes + " (تعديل يدوي)";
                existingRecord.IsManualEntry = true;
                _context.Update(existingRecord);
            }
            else
            {
                var attendance = new Attendance
                {
                    EmployeeId = employeeId,
                    Date = date.Date,
                    DayName = date.ToString("dddd", new System.Globalization.CultureInfo("ar-SA")),
                    TimeIn = timeIn,
                    TimeOut = timeOut,
                    Notes = notes,
                    IsManualEntry = true
                };
                _context.Add(attendance);
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "تم حفظ سجل الحضور";
            return RedirectToAction(nameof(Index));
        }
    }
}