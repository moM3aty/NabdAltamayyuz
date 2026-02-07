using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Data;
using NabdAltamayyuz.Models;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
// استخدام اسم مستعار لتجنب التعارض
using MyTaskStatus = NabdAltamayyuz.Models.TaskStatus;

namespace NabdAltamayyuz.Controllers
{
    [Authorize]
    public class TasksController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public TasksController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: Tasks (مع الفلترة والبحث والإحصائيات)
        public async Task<IActionResult> Index(string searchString, int? companyId, string status, DateTime? fromDate, DateTime? toDate)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var userRole = User.FindFirstValue(ClaimTypes.Role);

            var query = _context.WorkTasks
                .Include(t => t.AssignedTo)
                .ThenInclude(u => u.Company)
                .Include(t => t.CreatedBy)
                .AsQueryable();

            // 1. تحديد النطاق (Scope)
            if (userRole == "Employee")
            {
                query = query.Where(t => t.AssignedToId == userId);
            }
            else // SuperAdmin, CompanyAdmin, SubAdmin
            {
                if (userRole == "SuperAdmin")
                {
                    if (companyId.HasValue)
                    {
                        // جلب الشركة وفروعها إذا كانت رئيسية
                        var subIds = await _context.Companies.Where(c => c.ParentCompanyId == companyId).Select(c => c.Id).ToListAsync();
                        subIds.Add(companyId.Value);
                        query = query.Where(t => subIds.Contains(t.AssignedTo.CompanyId.Value));
                    }
                    ViewBag.Companies = new SelectList(await _context.Companies.Where(c => c.ParentCompanyId == null).ToListAsync(), "Id", "Name", companyId);
                }
                else
                {
                    var currentUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                    if (currentUser?.CompanyId != null)
                    {
                        var subIds = await _context.Companies.Where(c => c.ParentCompanyId == currentUser.CompanyId).Select(c => c.Id).ToListAsync();
                        subIds.Add(currentUser.CompanyId.Value);

                        if (companyId.HasValue && subIds.Contains(companyId.Value))
                        {
                            query = query.Where(t => t.AssignedTo.CompanyId == companyId.Value);
                        }
                        else
                        {
                            query = query.Where(t => subIds.Contains(t.AssignedTo.CompanyId.Value));
                        }

                        // قائمة الشركات للفلتر
                        var myCompanies = await _context.Companies
                            .Where(c => subIds.Contains(c.Id))
                            .Select(c => new { c.Id, Name = c.ParentCompanyId == null ? c.Name : " -- " + c.Name })
                            .ToListAsync();
                        ViewBag.Companies = new SelectList(myCompanies, "Id", "Name", companyId);
                    }
                }
            }

            // 2. الفلترة
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(t => t.Title.Contains(searchString) || t.AssignedTo.FullName.Contains(searchString));
            }

            if (!string.IsNullOrEmpty(status) && Enum.TryParse(typeof(MyTaskStatus), status, out object statusVal))
            {
                query = query.Where(t => t.Status == (MyTaskStatus)statusVal);
            }

            if (fromDate.HasValue) query = query.Where(t => t.StartDate >= fromDate.Value);
            if (toDate.HasValue) query = query.Where(t => t.DueDate <= toDate.Value);

            // 3. الإحصائيات (للمقياس العلوي) - يتم حسابها بناءً على الفلترة الحالية أو الكل حسب الرغبة
            // هنا سنحسبها بناءً على النطاق العام للمستخدم قبل فلترة البحث الدقيق لتعطي نظرة عامة
            var statsQuery = query; // أو يمكن إعادة بناء استعلام أوسع للإحصائيات

            ViewBag.TotalTasks = await statsQuery.CountAsync();
            ViewBag.CompletedTasks = await statsQuery.CountAsync(t => t.IsCompleted);
            ViewBag.PendingTasks = await statsQuery.CountAsync(t => t.Status == MyTaskStatus.Pending);
            ViewBag.DelayedTasks = await statsQuery.CountAsync(t => t.Status == MyTaskStatus.Delayed || (!t.IsCompleted && t.DueDate < DateTime.Today));

            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentStatus = status;
            ViewBag.CurrentCompany = companyId;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

            return View(await query.OrderByDescending(t => t.DueDate).ToListAsync());
        }

        // GET: Tasks/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var task = await _context.WorkTasks
                .Include(t => t.AssignedTo)
                .Include(t => t.CreatedBy)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (task == null) return NotFound();

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            // التحقق من الصلاحية
            if (!User.IsInRole("SuperAdmin"))
            {
                bool isManager = User.IsInRole("CompanyAdmin") || User.IsInRole("SubAdmin");
                if (!isManager && task.AssignedToId != userId && task.CreatedById != userId)
                {
                    return Forbid();
                }
            }

            return View(task);
        }

        // POST: Tasks/UpdateStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, MyTaskStatus status, string? reason)
        {
            var task = await _context.WorkTasks.FindAsync(id);
            if (task == null) return NotFound();

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            if (task.AssignedToId != userId && !User.IsInRole("CompanyAdmin") && !User.IsInRole("SubAdmin") && !User.IsInRole("SuperAdmin"))
            {
                return Forbid();
            }

            task.Status = status;
            task.StatusReason = reason;
            task.IsCompleted = (status == MyTaskStatus.Completed);

            _context.Update(task);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم تحديث حالة المهمة بنجاح";
            return RedirectToAction(nameof(Details), new { id = task.Id });
        }

        // GET: Tasks/Create
        [Authorize(Roles = "SuperAdmin,CompanyAdmin,SubAdmin")]
        public async Task<IActionResult> Create()
        {
            if (User.IsInRole("SuperAdmin"))
            {
                ViewBag.Companies = new SelectList(await _context.Companies.ToListAsync(), "Id", "Name");
            }
            else
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);

                // جلب الشركة والفروع
                var myCompanies = await _context.Companies
                    .Where(c => c.Id == user.CompanyId || c.ParentCompanyId == user.CompanyId)
                    .Select(c => new { c.Id, Name = c.ParentCompanyId == null ? c.Name : " -- " + c.Name })
                    .ToListAsync();

                // إذا كان فرع فقط، يظهر الموظفين مباشرة، إذا رئيسي يظهر قائمة الفروع
                ViewBag.Companies = new SelectList(myCompanies, "Id", "Name", user.CompanyId);

                // تحميل موظفي الشركة الافتراضية
                var employees = await _context.Users
                    .Where(u => u.CompanyId == user.CompanyId && u.Role == UserRole.Employee)
                    .Select(u => new { Id = u.Id, Name = u.FullName })
                    .ToListAsync();
                ViewBag.Employees = new SelectList(employees, "Id", "Name");
            }
            return View();
        }

        // POST: Tasks/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,CompanyAdmin,SubAdmin")]
        public async Task<IActionResult> Create(WorkTask model, IFormFile? attachment, int? selectedCompanyId)
        {
            ModelState.Clear();

            if (string.IsNullOrWhiteSpace(model.Title)) ModelState.AddModelError("Title", "عنوان المهمة مطلوب");
            if (model.AssignedToId == 0) ModelState.AddModelError("AssignedToId", "يجب اختيار موظف");

            model.CreatedById = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            model.Status = MyTaskStatus.Pending;
            model.IsCompleted = false;

            if (model.StartDate == DateTime.MinValue) model.StartDate = DateTime.Now;
            if (model.DueDate == DateTime.MinValue) model.DueDate = DateTime.Now.AddDays(1);

            if (ModelState.IsValid)
            {
                if (attachment != null && attachment.Length > 0)
                {
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads/tasks");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(attachment.FileName);
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await attachment.CopyToAsync(stream);
                    }
                    model.AttachmentPath = uniqueFileName;
                }

                _context.Add(model);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم إسناد المهمة بنجاح";
                return RedirectToAction(nameof(Index));
            }

            await PopulateLists(model.AssignedToId);
            return View(model);
        }

        // GET: Tasks/Edit/5
        [Authorize(Roles = "SuperAdmin,CompanyAdmin,SubAdmin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var task = await _context.WorkTasks
                .Include(t => t.AssignedTo)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (task == null) return NotFound();

            if (!User.IsInRole("SuperAdmin"))
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);

                // التحقق من أن المهمة تابعة لشركته أو فروعه
                var isAllowed = await _context.Companies.AnyAsync(c => c.Id == task.AssignedTo.CompanyId && (c.Id == user.CompanyId || c.ParentCompanyId == user.CompanyId));
                if (!isAllowed) return Forbid();
            }

            await PopulateLists(task.AssignedToId, task.AssignedTo.CompanyId);
            return View(task);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,CompanyAdmin,SubAdmin")]
        public async Task<IActionResult> Edit(int id, WorkTask model, IFormFile? attachment)
        {
            if (id != model.Id) return NotFound();

            var task = await _context.WorkTasks.FindAsync(id);
            if (task == null) return NotFound();

            ModelState.Clear();
            if (string.IsNullOrWhiteSpace(model.Title)) ModelState.AddModelError("Title", "عنوان المهمة مطلوب");

            if (ModelState.IsValid)
            {
                try
                {
                    task.Title = model.Title;
                    task.Description = model.Description;
                    task.StartDate = model.StartDate;
                    task.DueDate = model.DueDate;
                    task.AssignedToId = model.AssignedToId;

                    if (attachment != null && attachment.Length > 0)
                    {
                        string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads/tasks");
                        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                        string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(attachment.FileName);
                        string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await attachment.CopyToAsync(stream);
                        }
                        task.AttachmentPath = uniqueFileName;
                    }

                    _context.Update(task);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "تم تعديل المهمة";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.WorkTasks.Any(e => e.Id == id)) return NotFound();
                    else throw;
                }
            }

            await PopulateLists(model.AssignedToId);
            return View(model);
        }

        // Helper
        private async Task PopulateLists(int? selectedEmployeeId = null, int? companyId = null)
        {
            // منطق تعبئة القوائم (الشركات والموظفين) بناءً على الصلاحيات
            if (User.IsInRole("SuperAdmin"))
            {
                ViewBag.Companies = new SelectList(await _context.Companies.ToListAsync(), "Id", "Name", companyId);
            }
            else
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);
                var myCompanies = await _context.Companies
                    .Where(c => c.Id == user.CompanyId || c.ParentCompanyId == user.CompanyId)
                    .Select(c => new { c.Id, Name = c.ParentCompanyId == null ? c.Name : " -- " + c.Name })
                    .ToListAsync();
                ViewBag.Companies = new SelectList(myCompanies, "Id", "Name", companyId);
            }

            if (companyId != null)
            {
                var emps = await _context.Users.Where(u => u.CompanyId == companyId && u.Role == UserRole.Employee).ToListAsync();
                ViewBag.Employees = new SelectList(emps, "Id", "FullName", selectedEmployeeId);
            }
            else if (selectedEmployeeId != null)
            {
                var emp = await _context.Users.FindAsync(selectedEmployeeId);
                if (emp != null)
                {
                    var emps = await _context.Users.Where(u => u.CompanyId == emp.CompanyId && u.Role == UserRole.Employee).ToListAsync();
                    ViewBag.Employees = new SelectList(emps, "Id", "FullName", selectedEmployeeId);
                }
            }
        }

        // AJAX Helper
        [HttpGet]
        public async Task<IActionResult> GetEmployeesByCompany(int companyId)
        {
            // تحقق من الصلاحية للمشرفين
            if (!User.IsInRole("SuperAdmin"))
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);
                var isAllowed = companyId == user.CompanyId || await _context.Companies.AnyAsync(c => c.Id == companyId && c.ParentCompanyId == user.CompanyId);
                if (!isAllowed) return Forbid();
            }

            var employees = await _context.Users
                .Where(u => u.CompanyId == companyId && u.Role == UserRole.Employee)
                .OrderBy(u => u.FullName)
                .Select(u => new { id = u.Id, name = u.FullName })
                .ToListAsync();
            return Json(employees);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var task = await _context.WorkTasks.FindAsync(id);
            if (task != null)
            {
                _context.WorkTasks.Remove(task);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم حذف المهمة";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Complete(int id)
        {
            var task = await _context.WorkTasks.FindAsync(id);
            if (task == null) return NotFound();
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            // الموظف أو المشرف يمكنه الإنجاز
            bool isAuthorized = task.AssignedToId == userId || User.IsInRole("CompanyAdmin") || User.IsInRole("SuperAdmin") || User.IsInRole("SubAdmin");
            if (!isAuthorized) return Forbid();

            task.IsCompleted = true;
            task.Status = MyTaskStatus.Completed;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
    }
}