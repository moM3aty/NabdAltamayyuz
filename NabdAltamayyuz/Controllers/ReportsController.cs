using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Reports/Index
        public async Task<IActionResult> Index()
        {
            if (User.IsInRole("SuperAdmin"))
            {
                ViewBag.Companies = new SelectList(await _context.Companies.Where(c => c.ParentCompanyId == null).ToListAsync(), "Id", "Name");
            }
            else if (!User.IsInRole("Employee"))
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);

                var companies = await _context.Companies
                    .Where(c => c.Id == user.CompanyId || c.ParentCompanyId == user.CompanyId)
                    .Select(c => new { c.Id, Name = c.ParentCompanyId == null ? c.Name : " -- " + c.Name })
                    .ToListAsync();
                ViewBag.Companies = new SelectList(companies, "Id", "Name");

                var employees = await _context.Users.Where(u => u.CompanyId == user.CompanyId && u.Role == UserRole.Employee).ToListAsync();
                ViewBag.Employees = new SelectList(employees, "Id", "FullName");
            }

            return View();
        }

        // GET: Reports/Generate
        public async Task<IActionResult> Generate(string type, DateTime? from, DateTime? to, int? companyId, int? employeeId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var userRole = User.FindFirstValue(ClaimTypes.Role);

            ViewBag.ReportType = type;
            ViewBag.FromDate = from?.ToString("yyyy-MM-dd") ?? "-";
            ViewBag.ToDate = to?.ToString("yyyy-MM-dd") ?? "-";
            ViewBag.PrintDate = DateTime.UtcNow.AddHours(3);

            // التعديل 4: ضمان ظهور اسم شركة الموظف بشكل دائم
            var currentUser = await _context.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Id == userId);

            if (companyId.HasValue)
            {
                var comp = await _context.Companies.FindAsync(companyId.Value);
                ViewBag.CompanyName = comp?.Name;
            }
            else
            {
                // إذا كان موظف أو لم يتم تحديد شركة، اجلب اسم شركته الفعلية
                ViewBag.CompanyName = currentUser?.Company?.Name ?? "نبض التميز الذهبي";
            }

            IQueryable<int> allowedCompanyIds;
            if (userRole == "SuperAdmin")
            {
                if (companyId.HasValue)
                {
                    var subIds = await _context.Companies.Where(c => c.ParentCompanyId == companyId).Select(c => c.Id).ToListAsync();
                    subIds.Add(companyId.Value);
                    allowedCompanyIds = subIds.AsQueryable();
                }
                else
                {
                    allowedCompanyIds = _context.Companies.Select(c => c.Id);
                }
            }
            else if (userRole != "Employee")
            {
                var subIds = await _context.Companies.Where(c => c.ParentCompanyId == currentUser.CompanyId).Select(c => c.Id).ToListAsync();
                subIds.Add(currentUser.CompanyId.Value);

                if (companyId.HasValue && subIds.Contains(companyId.Value))
                    allowedCompanyIds = new[] { companyId.Value }.AsQueryable();
                else
                    allowedCompanyIds = subIds.AsQueryable();
            }
            else
            {
                allowedCompanyIds = Enumerable.Empty<int>().AsQueryable();
            }

            if (type == "attendance")
            {
                var query = _context.Attendances.Include(a => a.Employee).ThenInclude(e => e.Company).AsQueryable();

                if (userRole == "Employee")
                {
                    query = query.Where(a => a.EmployeeId == userId);
                }
                else
                {
                    query = query.Where(a => allowedCompanyIds.Contains(a.Employee.CompanyId.Value));
                    if (employeeId.HasValue) query = query.Where(a => a.EmployeeId == employeeId.Value);
                }

                if (from.HasValue) query = query.Where(a => a.Date >= from.Value);
                if (to.HasValue) query = query.Where(a => a.Date <= to.Value);

                var data = await query.OrderByDescending(a => a.Date).ToListAsync();
                return View("PrintAttendance", data);
            }

            else if (type == "tasks")
            {
                var query = _context.WorkTasks
                    .Include(t => t.AssignedTo).ThenInclude(e => e.Company)
                    .Include(t => t.CreatedBy)
                    .AsQueryable();

                if (userRole == "Employee")
                {
                    // التعديل 4: التأكد من جلب المهام للموظف بنجاح
                    query = query.Where(t => t.AssignedToId == userId);
                }
                else
                {
                    query = query.Where(t => allowedCompanyIds.Contains(t.AssignedTo.CompanyId.Value));
                    if (employeeId.HasValue) query = query.Where(t => t.AssignedToId == employeeId.Value);
                }

                if (from.HasValue) query = query.Where(t => t.DueDate >= from.Value);
                if (to.HasValue) query = query.Where(t => t.DueDate <= to.Value);

                var data = await query.OrderByDescending(t => t.DueDate).ToListAsync();
                return View("PrintTasks", data);
            }

            else if (type == "subscriptions")
            {
                if (userRole == "Employee") return Forbid();

                var query = _context.Companies
                    .Include(c => c.Employees)
                    .Include(c => c.SubCompanies)
                    .Where(c => allowedCompanyIds.Contains(c.Id))
                    .AsQueryable();

                var data = await query.OrderByDescending(c => c.SubscriptionEndDate).ToListAsync();
                ViewBag.TotalRevenue = data.Sum(c => c.TotalPricePerEmployee * c.AllowedEmployees);

                return View("PrintSubscriptions", data);
            }

            else if (type == "employees")
            {
                if (userRole == "Employee") return Forbid();

                var query = _context.Users
                    .Include(u => u.Company)
                    .Where(u => allowedCompanyIds.Contains(u.CompanyId.Value) && u.Role == UserRole.Employee)
                    .AsQueryable();

                if (employeeId.HasValue) query = query.Where(u => u.Id == employeeId.Value);

                var data = await query.OrderBy(u => u.CompanyId).ThenBy(u => u.FullName).ToListAsync();
                return View("PrintEmployees", data);
            }

            return BadRequest("نوع التقرير غير صالح");
        }
    }
}