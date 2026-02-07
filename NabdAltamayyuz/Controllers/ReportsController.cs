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
            // تجهيز قائمة الشركات للفلتر في صفحة التقارير
            if (User.IsInRole("SuperAdmin"))
            {
                ViewBag.Companies = new SelectList(await _context.Companies.Where(c => c.ParentCompanyId == null).ToListAsync(), "Id", "Name");
            }
            else if (!User.IsInRole("Employee"))
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);

                // جلب الشركة الرئيسية والفروع
                var companies = await _context.Companies
                    .Where(c => c.Id == user.CompanyId || c.ParentCompanyId == user.CompanyId)
                    .Select(c => new { c.Id, Name = c.ParentCompanyId == null ? c.Name : " -- " + c.Name })
                    .ToListAsync();
                ViewBag.Companies = new SelectList(companies, "Id", "Name");
            }

            return View();
        }

        // GET: Reports/Generate
        public async Task<IActionResult> Generate(string type, DateTime? from, DateTime? to, int? companyId, string searchString)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var userRole = User.FindFirstValue(ClaimTypes.Role);

            ViewBag.ReportType = type;
            ViewBag.FromDate = from?.ToString("yyyy-MM-dd") ?? "-";
            ViewBag.ToDate = to?.ToString("yyyy-MM-dd") ?? "-";
            ViewBag.PrintDate = DateTime.Now;

            // تحديد نطاق الشركات المسموح بها
            IQueryable<int> allowedCompanyIds;
            if (userRole == "SuperAdmin")
            {
                if (companyId.HasValue)
                {
                    // إذا اختار شركة محددة، نجلبها مع فروعها
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
                var currentUser = await _context.Users.FindAsync(userId);
                var subIds = await _context.Companies.Where(c => c.ParentCompanyId == currentUser.CompanyId).Select(c => c.Id).ToListAsync();
                subIds.Add(currentUser.CompanyId.Value);

                if (companyId.HasValue && subIds.Contains(companyId.Value))
                    allowedCompanyIds = new[] { companyId.Value }.AsQueryable();
                else
                    allowedCompanyIds = subIds.AsQueryable();
            }
            else // Employee
            {
                allowedCompanyIds = Enumerable.Empty<int>().AsQueryable();
            }

            // 1. تقرير الحضور
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
                    if (!string.IsNullOrEmpty(searchString)) query = query.Where(a => a.Employee.FullName.Contains(searchString));
                }

                if (from.HasValue) query = query.Where(a => a.Date >= from.Value);
                if (to.HasValue) query = query.Where(a => a.Date <= to.Value);

                var data = await query.OrderByDescending(a => a.Date).ToListAsync();
                return View("PrintAttendance", data);
            }

            // 2. تقرير المهام
            else if (type == "tasks")
            {
                var query = _context.WorkTasks.Include(t => t.AssignedTo).ThenInclude(e => e.Company).Include(t => t.CreatedBy).AsQueryable();

                if (userRole == "Employee")
                {
                    query = query.Where(t => t.AssignedToId == userId);
                }
                else
                {
                    query = query.Where(t => allowedCompanyIds.Contains(t.AssignedTo.CompanyId.Value));
                }

                if (from.HasValue) query = query.Where(t => t.DueDate >= from.Value);
                if (to.HasValue) query = query.Where(t => t.DueDate <= to.Value);

                var data = await query.OrderByDescending(t => t.DueDate).ToListAsync();
                return View("PrintTasks", data);
            }

            // 3. تقرير الاشتراكات والفواتير (للمشرفين والسوبر أدمن)
            else if (type == "subscriptions")
            {
                if (userRole == "Employee") return Forbid();

                var query = _context.Companies
                    .Include(c => c.Employees)
                    .Include(c => c.SubCompanies)
                    .Where(c => allowedCompanyIds.Contains(c.Id))
                    .AsQueryable();

                // للسوبر أدمن: يمكنه رؤية الشركات الرئيسية وفروعها بشكل منظم
                // للمشرف: يرى شركته وفروعها

                var data = await query.OrderByDescending(c => c.SubscriptionEndDate).ToListAsync();

                // حساب إجماليات للتقرير
                ViewBag.TotalRevenue = data.Sum(c => c.TotalPricePerEmployee * c.AllowedEmployees); // معادلة تقريبية

                return View("PrintSubscriptions", data);
            }

            // 4. تقرير الموظفين الشامل
            else if (type == "employees")
            {
                if (userRole == "Employee") return Forbid();

                var query = _context.Users
                    .Include(u => u.Company)
                    .Where(u => allowedCompanyIds.Contains(u.CompanyId.Value) && u.Role == UserRole.Employee)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(searchString)) query = query.Where(u => u.FullName.Contains(searchString));

                var data = await query.OrderBy(u => u.CompanyId).ThenBy(u => u.FullName).ToListAsync();
                return View("PrintEmployees", data); // تحتاج لإنشاء هذا الـ View إذا لم يكن موجوداً، أو استخدام تصميم مشابه
            }

            return BadRequest("نوع التقرير غير صالح");
        }
    }
}