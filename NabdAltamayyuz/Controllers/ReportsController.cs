using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Data;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NabdAltamayyuz.Controllers
{
    // السماح لكل المستخدمين المسجلين بالدخول، ويتم فلترة البيانات داخلياً
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Reports/Index
        public IActionResult Index()
        {
            return View();
        }

        // GET: Reports/Generate
        public async Task<IActionResult> Generate(string type, DateTime? from, DateTime? to, string searchEmployee)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var userRole = User.FindFirstValue(ClaimTypes.Role);
            int? companyId = null;

            // تحديد النطاق (Scope)
            if (userRole == "Employee")
            {
                // الموظف لا يمكنه البحث عن غيره
                searchEmployee = "";
            }
            else if (userRole != "SuperAdmin") // CompanyAdmin & SubAdmin
            {
                var currentUser = await _context.Users.FindAsync(userId);
                companyId = currentUser?.CompanyId;
            }

            ViewBag.ReportType = type;
            ViewBag.FromDate = from?.ToString("yyyy-MM-dd") ?? "-";
            ViewBag.ToDate = to?.ToString("yyyy-MM-dd") ?? "-";
            ViewBag.PrintDate = DateTime.Now;

            if (type == "attendance")
            {
                var query = _context.Attendances.Include(a => a.Employee).AsQueryable();

                if (userRole == "Employee")
                {
                    query = query.Where(a => a.EmployeeId == userId);
                }
                else
                {
                    if (companyId != null) query = query.Where(a => a.Employee.CompanyId == companyId);
                    if (!string.IsNullOrEmpty(searchEmployee)) query = query.Where(a => a.Employee.FullName.Contains(searchEmployee));
                }

                if (from.HasValue) query = query.Where(a => a.Date >= from.Value);
                if (to.HasValue) query = query.Where(a => a.Date <= to.Value);

                var data = await query.OrderByDescending(a => a.Date).ToListAsync();
                return View("PrintAttendance", data);
            }
            else if (type == "tasks")
            {
                var query = _context.WorkTasks
                    .Include(t => t.AssignedTo)
                    .Include(t => t.CreatedBy)
                    .AsQueryable();

                if (userRole == "Employee")
                {
                    // الموظف يرى المهام المسندة إليه
                    query = query.Where(t => t.AssignedToId == userId);
                }
                else
                {
                    if (companyId != null) query = query.Where(t => t.AssignedTo.CompanyId == companyId);
                }

                if (from.HasValue) query = query.Where(t => t.DueDate >= from.Value);
                if (to.HasValue) query = query.Where(t => t.DueDate <= to.Value);

                var data = await query.OrderByDescending(t => t.DueDate).ToListAsync();
                return View("PrintTasks", data);
            }

            return BadRequest("نوع التقرير غير صالح");
        }
    }
}