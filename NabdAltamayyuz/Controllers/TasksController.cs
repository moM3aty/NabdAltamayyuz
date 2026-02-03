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

        // GET: Tasks (مع الفلترة والبحث)
        public async Task<IActionResult> Index(string searchString, int? companyId, string status)
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
            else if (userRole != "SuperAdmin") // CompanyAdmin & SubAdmin
            {
                var currentUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (currentUser?.CompanyId != null)
                {
                    query = query.Where(t => t.AssignedTo.CompanyId == currentUser.CompanyId);

                    // تجهيز قائمة الموظفين للفلتر في الـ View
                    ViewBag.Employees = new SelectList(await _context.Users
                        .Where(u => u.CompanyId == currentUser.CompanyId && u.Role == UserRole.Employee)
                        .Select(u => new { Id = u.Id, Name = u.FullName })
                        .ToListAsync(), "Id", "Name");
                }
            }
            else // SuperAdmin
            {
                if (companyId.HasValue) query = query.Where(t => t.AssignedTo.CompanyId == companyId);

                ViewBag.Companies = new SelectList(await _context.Companies.ToListAsync(), "Id", "Name", companyId);
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

            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentStatus = status;
            ViewBag.CurrentCompany = companyId;

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
                // يسمح للمشرفين ولصاحب المهمة
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
                var currentUser = await _context.Users.FindAsync(userId);

                var employees = await _context.Users
                    .Where(u => u.CompanyId == currentUser.CompanyId && u.Role == UserRole.Employee)
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
        public async Task<IActionResult> Create(WorkTask model, IFormFile? attachment)
        {
            // 1. تنظيف الـ ModelState (هام جداً للحفظ)
            ModelState.Clear();

            // 2. التحقق اليدوي للحقول الإجبارية
            if (string.IsNullOrWhiteSpace(model.Title)) ModelState.AddModelError("Title", "عنوان المهمة مطلوب");
            if (model.AssignedToId == 0) ModelState.AddModelError("AssignedToId", "يجب اختيار موظف");

            // 3. إعداد البيانات الافتراضية
            model.CreatedById = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            model.Status = MyTaskStatus.Pending;
            model.IsCompleted = false;

            if (model.StartDate == DateTime.MinValue) model.StartDate = DateTime.Now;
            if (model.DueDate == DateTime.MinValue) model.DueDate = DateTime.Now.AddDays(1);

            if (ModelState.IsValid)
            {
                // رفع المرفق
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

            // إعادة تعبئة القوائم عند الخطأ
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

            // التحقق من الصلاحية
            if (!User.IsInRole("SuperAdmin"))
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(userId);
                if (task.AssignedTo.CompanyId != currentUser.CompanyId) return Forbid();
            }

            await PopulateLists(task.AssignedToId, task.AssignedTo.CompanyId);
            return View(task);
        }

        // POST: Tasks/Edit/5
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

        // Helper: Populate Dropdowns
        private async Task PopulateLists(int? selectedEmployeeId = null, int? companyIdForSuperAdmin = null)
        {
            if (User.IsInRole("SuperAdmin"))
            {
                ViewBag.Companies = new SelectList(await _context.Companies.ToListAsync(), "Id", "Name", companyIdForSuperAdmin);

                // إذا تم تحديد شركة أو موظف، نعرض الموظفين
                if (companyIdForSuperAdmin != null)
                {
                    var emps = await _context.Users.Where(u => u.CompanyId == companyIdForSuperAdmin && u.Role == UserRole.Employee).ToListAsync();
                    ViewBag.Employees = new SelectList(emps, "Id", "FullName", selectedEmployeeId);
                }
                else if (selectedEmployeeId != null && selectedEmployeeId != 0)
                {
                    var emp = await _context.Users.FindAsync(selectedEmployeeId);
                    if (emp != null)
                    {
                        var emps = await _context.Users.Where(u => u.CompanyId == emp.CompanyId && u.Role == UserRole.Employee).ToListAsync();
                        ViewBag.Employees = new SelectList(emps, "Id", "FullName", selectedEmployeeId);
                    }
                }
            }
            else
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(userId);
                var emps = await _context.Users.Where(u => u.CompanyId == currentUser.CompanyId && u.Role == UserRole.Employee).ToListAsync();
                ViewBag.Employees = new SelectList(emps, "Id", "FullName", selectedEmployeeId);
            }
        }

        // GET: Employees by Company (AJAX)
        [HttpGet]
        public async Task<IActionResult> GetEmployeesByCompany(int companyId)
        {
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

            // تحقق بسيط من الصلاحية
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            if (task.AssignedToId != userId && !User.IsInRole("CompanyAdmin") && !User.IsInRole("SuperAdmin") && !User.IsInRole("SubAdmin")) return Forbid();

            task.IsCompleted = true;
            task.Status = MyTaskStatus.Completed;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
    }
}