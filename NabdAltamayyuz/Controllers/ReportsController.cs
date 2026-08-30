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

            // استخدام توقيت السعودية للطباعة
            ViewBag.PrintDate = DateTime.UtcNow.AddHours(3);

            var currentUser = await _context.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Id == userId);

            // تحديد اسم الشركة لطباعته في ترويسة التقرير
            if (companyId.HasValue)
            {
                var comp = await _context.Companies.FindAsync(companyId.Value);
                ViewBag.CompanyName = comp?.Name;
            }
            else
            {
                // ضمان ظهور اسم شركة الموظف/المشرف بشكل دائم في الطباعة
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
                // تحديد فترة التقرير (إذا لم يتم التحديد نختار من بداية الشهر الحالي حتى اليوم)
                var targetFrom = from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                var targetTo = to ?? DateTime.Today;

                // 1. جلب الموظفين المطلوبين بناءً على الصلاحيات والفلتر
                var empQuery = _context.Users.Include(u => u.Company)
                    .Where(u => u.Role == UserRole.Employee);

                if (userRole == "Employee")
                {
                    empQuery = empQuery.Where(u => u.Id == userId);
                }
                else
                {
                    empQuery = empQuery.Where(u => u.CompanyId != null && allowedCompanyIds.Contains(u.CompanyId.Value));
                    if (employeeId.HasValue) empQuery = empQuery.Where(u => u.Id == employeeId.Value);
                }

                var employees = await empQuery.ToListAsync();
                var empIds = employees.Select(e => e.Id).ToList();

                // 2. جلب سجلات الحضور الفعلية المتوفرة في قاعدة البيانات
                var actualAttendances = await _context.Attendances
                    .Where(a => empIds.Contains(a.EmployeeId) && a.Date >= targetFrom && a.Date <= targetTo)
                    .ToListAsync();

                // 3. جلب الإجازات المعتمدة خلال هذه الفترة
                var leaves = await _context.LeaveRequests
                    .Where(l => empIds.Contains(l.EmployeeId) && l.Status == LeaveStatus.Approved && l.StartDate <= targetTo && l.EndDate >= targetFrom)
                    .ToListAsync();

                // 4. بناء التقرير الشامل (تسلسل الأيام لكل موظف)
                var comprehensiveData = new List<Attendance>();

                foreach (var emp in employees)
                {
                    // حلقة تكرارية تمر على كل يوم في الفترة المحددة
                    for (var date = targetFrom.Date; date <= targetTo.Date; date = date.AddDays(1))
                    {
                        var existingAtt = actualAttendances.FirstOrDefault(a => a.EmployeeId == emp.Id && a.Date.Date == date);

                        if (existingAtt != null)
                        {
                            existingAtt.Employee = emp; // ربط الموظف لضمان ظهور بياناته
                            comprehensiveData.Add(existingAtt);
                        }
                        else
                        {
                            // إذا لم يجد سجل للحضور في هذا اليوم، يقوم بإنشاء سجل افتراضي لمعالجته كغياب أو إجازة
                            comprehensiveData.Add(new Attendance
                            {
                                EmployeeId = emp.Id,
                                Employee = emp,
                                Date = date,
                                DayName = date.ToString("dddd", new System.Globalization.CultureInfo("ar-SA")),
                                TimeIn = null, // لا يوجد وقت دخول = غائب (إلا إذا كان لديه إجازة سيتم فحصها في الواجهة)
                                TimeOut = null,
                                Notes = null
                            });
                        }
                    }
                }

                // ترتيب التقرير حسب التاريخ (من الأحدث للأقدم) ثم باسم الموظف
                var data = comprehensiveData.OrderByDescending(a => a.Date).ThenBy(a => a.Employee.FullName).ToList();

                ViewBag.ReportLeaves = leaves;
                ViewBag.FromDate = targetFrom.ToString("yyyy-MM-dd");
                ViewBag.ToDate = targetTo.ToString("yyyy-MM-dd");

                return View("PrintAttendance", data);
            }

            else if (type == "tasks")
            {
                var query = _context.WorkTasks
                    .Include(t => t.AssignedTo).ThenInclude(e => e.Company)
                    .Include(t => t.CreatedBy)
                    .AsQueryable();

                // إصلاح فلترة المهام للموظف لضمان ظهورها جميعاً في الطباعة
                if (userRole == "Employee") query = query.Where(t => t.AssignedToId == userId);
                else
                {
                    query = query.Where(t => t.AssignedTo.CompanyId != null && allowedCompanyIds.Contains(t.AssignedTo.CompanyId.Value));
                    if (employeeId.HasValue) query = query.Where(t => t.AssignedToId == employeeId.Value);
                }

                if (from.HasValue) query = query.Where(t => t.DueDate >= from.Value);
                if (to.HasValue) query = query.Where(t => t.DueDate <= to.Value);

                var data = await query.OrderByDescending(t => t.DueDate).ToListAsync();

                // حساب النسبة المئوية لإرسالها لملف الطباعة
                int total = data.Count;
                int completed = data.Count(t => t.IsCompleted);
                ViewBag.CompletionPercentage = total > 0 ? Math.Round((double)completed / total * 100, 1) : 0;

                return View("PrintTasks", data);
            }

            else if (type == "leaves")
            {
                var query = _context.LeaveRequests
                    .Include(l => l.Employee).ThenInclude(e => e.Company)
                    .AsQueryable();

                if (userRole == "Employee") query = query.Where(l => l.EmployeeId == userId);
                else
                {
                    query = query.Where(l => l.Employee.CompanyId != null && allowedCompanyIds.Contains(l.Employee.CompanyId.Value));
                    if (employeeId.HasValue) query = query.Where(l => l.EmployeeId == employeeId.Value);
                }

                if (from.HasValue) query = query.Where(l => l.StartDate >= from.Value);
                if (to.HasValue) query = query.Where(l => l.StartDate <= to.Value);

                var data = await query.OrderByDescending(l => l.CreatedAt).ToListAsync();
                return View("PrintLeaves", data);
            }

            else if (type == "interaction")
            {
                if (userRole == "Employee") return Forbid();

                var targetMonth = from ?? DateTime.Today;

                var empQuery = _context.Users
                    .Include(u => u.Company)
                    .Where(u => u.Role == UserRole.Employee && u.CompanyId != null && allowedCompanyIds.Contains(u.CompanyId.Value));

                if (employeeId.HasValue)
                    empQuery = empQuery.Where(u => u.Id == employeeId.Value);

                var employees = await empQuery.ToListAsync();
                var empIds = employees.Select(e => e.Id).ToList();

                // استدعاء دالة المزامنة لحساب الحضور والمهام تلقائياً قبل عرض التقرير
                await SyncMonthlyInteractionsAsync(empIds, targetMonth);

                // جلب البيانات بعد تحديثها
                var interactions = await _context.MonthlyInteractions
                    .Include(m => m.Employee)
                    .Where(m => empIds.Contains(m.EmployeeId) && m.MonthYear.Year == targetMonth.Year && m.MonthYear.Month == targetMonth.Month)
                    .ToListAsync();

                // الترتيب بناءً على نسبة التفاعل
                var data = interactions.OrderByDescending(m => m.InteractionPercentage).ToList();

                return View("PrintInteraction", data);
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

        // إضافة دالة ترحيل التفاعل لمنصة العمل عن بعد (Teleworks)
        [HttpPost]
        [Authorize(Roles = "SuperAdmin,CompanyAdmin,SubAdmin")]
        private async Task SyncMonthlyInteractionsAsync(List<int> employeeIds, DateTime targetMonth)
        {
            foreach (var empId in employeeIds)
            {
                // 1. البحث عن سجل التفاعل لهذا الشهر، أو إنشاؤه إذا لم يكن موجوداً
                var interaction = await _context.MonthlyInteractions
                    .FirstOrDefaultAsync(m => m.EmployeeId == empId && m.MonthYear.Year == targetMonth.Year && m.MonthYear.Month == targetMonth.Month);

                if (interaction == null)
                {
                    interaction = new MonthlyInteraction
                    {
                        EmployeeId = empId,
                        MonthYear = new DateTime(targetMonth.Year, targetMonth.Month, 1),
                        RequiredHours = 176, // المقرر التلقائي
                        IsManuallyEdited = false
                    };
                    _context.MonthlyInteractions.Add(interaction);
                }

                // 2. تحديث المنجز والمهام فقط إذا لم يقم المشرف بتعديلها يدوياً
                if (!interaction.IsManuallyEdited)
                {
                    // حساب ساعات الحضور الفعلية خلال الشهر
                    var attendances = await _context.Attendances
                        .Where(a => a.EmployeeId == empId && a.Date.Year == targetMonth.Year && a.Date.Month == targetMonth.Month && a.TimeIn != null && a.TimeOut != null)
                        .ToListAsync();

                    double totalHours = 0;
                    foreach (var att in attendances)
                    {
                        totalHours += (att.TimeOut.Value - att.TimeIn.Value).TotalHours;
                    }
                    interaction.CompletedHours = Math.Round(totalHours, 2);

                    // حساب المهام المسندة والمنجزة خلال الشهر
                    var tasks = await _context.WorkTasks
                        .Where(t => t.AssignedToId == empId && t.DueDate.Year == targetMonth.Year && t.DueDate.Month == targetMonth.Month)
                        .ToListAsync();

                    interaction.TotalTasks = tasks.Count;
                    interaction.CompletedTasks = tasks.Count(t => t.IsCompleted);
                }
            }

            await _context.SaveChangesAsync();
        }
    }
}