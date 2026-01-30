using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Data;
using NabdAltamayyuz.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace NabdAltamayyuz.Controllers
{
    [Authorize(Roles = "CompanyAdmin,SuperAdmin")]
    public class AttendanceController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AttendanceController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Attendance Sheet
        public async Task<IActionResult> Index(string searchEmployee, DateTime? startDate, DateTime? endDate)
        {
            var query = _context.Attendances
                .Include(a => a.Employee)
                .AsQueryable();

            // Filter by Name
            if (!string.IsNullOrEmpty(searchEmployee))
            {
                query = query.Where(a => a.Employee.FullName.Contains(searchEmployee));
            }

            // Filter by Date Range
            if (startDate.HasValue)
            {
                query = query.Where(a => a.Date >= startDate.Value);
                ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            }
            if (endDate.HasValue)
            {
                query = query.Where(a => a.Date <= endDate.Value);
                ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");
            }

            // Order by most recent
            var data = await query.OrderByDescending(a => a.Date).ToListAsync();

            return View(data);
        }

        // POST: Attendance/ManualEntry (For Admins to fix/add records)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManualEntry(int employeeId, DateTime date, DateTime timeIn, DateTime? timeOut, string notes)
        {
            var existingRecord = await _context.Attendances
                .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Date == date.Date);

            if (existingRecord != null)
            {
                // Update existing
                existingRecord.TimeIn = timeIn;
                existingRecord.TimeOut = timeOut;
                existingRecord.Notes = notes + " (تعديل يدوي)";
                existingRecord.IsManualEntry = true;
                _context.Update(existingRecord);
            }
            else
            {
                // Create new
                var attendance = new Attendance
                {
                    EmployeeId = employeeId,
                    Date = date.Date,
                    TimeIn = timeIn,
                    TimeOut = timeOut,
                    Notes = notes,
                    IsManualEntry = true
                };
                _context.Add(attendance);
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "تم حفظ سجل الحضور";
            return RedirectToAction(nameof(Index)); // Or redirect to Dashboard
        }
    }
}