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
// حل مشكلة تعارض TaskStatus باستخدام اسم مستعار
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

        // GET: Tasks
        public async Task<IActionResult> Index()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var userRole = User.FindFirstValue(ClaimTypes.Role);

            IQueryable<WorkTask> query = _context.WorkTasks
                .Include(t => t.AssignedTo)
                .Include(t => t.CreatedBy);

            if (userRole == "Employee")
            {
                query = query.Where(t => t.AssignedToId == userId);
            }
            else if (userRole == "CompanyAdmin" || userRole == "SubAdmin")
            {
                var currentUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (currentUser?.CompanyId != null)
                {
                    // عرض المهام المرتبطة بموظفي الشركة
                    query = query.Where(t => t.AssignedTo.CompanyId == currentUser.CompanyId);
                }
            }
            // SuperAdmin يرى كل المهام

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

            // التحقق من الصلاحية
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            if (!User.IsInRole("SuperAdmin") && !User.IsInRole("CompanyAdmin") &&
                task.AssignedToId != userId && task.CreatedById != userId)
            {
                return Forbid();
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

            // السماح لصاحب المهمة أو المشرفين بالتعديل
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
            await PopulateEmployeesDropDownList();
            return View();
        }

        // POST: Tasks/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,CompanyAdmin,SubAdmin")]
        public async Task<IActionResult> Create(WorkTask model, IFormFile? attachment)
        {
            // تنظيف الحقول غير المطلوبة في الـ Form
            ModelState.Remove(nameof(model.AssignedTo));
            ModelState.Remove(nameof(model.CreatedBy));
            ModelState.Remove(nameof(model.AttachmentPath));

            if (ModelState.IsValid)
            {
                // رفع المرفقات
                if (attachment != null && attachment.Length > 0)
                {
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads/tasks");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(attachment.FileName);
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await attachment.CopyToAsync(fileStream);
                    }
                    model.AttachmentPath = uniqueFileName;
                }

                model.CreatedById = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                model.Status = MyTaskStatus.Pending;

                if (model.StartDate == DateTime.MinValue) model.StartDate = DateTime.Now;

                _context.Add(model);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم إسناد المهمة بنجاح";
                return RedirectToAction(nameof(Index));
            }

            // إعادة ملء القائمة عند الخطأ
            await PopulateEmployeesDropDownList(model.AssignedToId);
            return View(model);
        }

        // GET: Tasks/Edit/5
        [Authorize(Roles = "SuperAdmin,CompanyAdmin,SubAdmin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var task = await _context.WorkTasks.FindAsync(id);
            if (task == null) return NotFound();

            await PopulateEmployeesDropDownList(task.AssignedToId);
            return View(task);
        }

        // POST: Tasks/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,CompanyAdmin,SubAdmin")]
        public async Task<IActionResult> Edit(int id, WorkTask model, IFormFile? attachment)
        {
            if (id != model.Id) return NotFound();

            // تجاهل الحقول المرجعية
            ModelState.Remove(nameof(model.AssignedTo));
            ModelState.Remove(nameof(model.CreatedBy));

            if (ModelState.IsValid)
            {
                try
                {
                    var task = await _context.WorkTasks.FindAsync(id);
                    if (task == null) return NotFound();

                    // تحديث البيانات
                    task.Title = model.Title;
                    task.Description = model.Description;
                    task.DueDate = model.DueDate;
                    task.AssignedToId = model.AssignedToId;

                    // تحديث المرفق إذا وجد
                    if (attachment != null && attachment.Length > 0)
                    {
                        string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads/tasks");
                        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                        string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(attachment.FileName);
                        string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await attachment.CopyToAsync(fileStream);
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

            await PopulateEmployeesDropDownList(model.AssignedToId);
            return View(model);
        }

        // POST: Delete
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

        // دالة مساعدة لملء قائمة الموظفين
        private async Task PopulateEmployeesDropDownList(object selectedEmployee = null)
        {
            var usersQuery = _context.Users.AsQueryable();

            if (User.IsInRole("SuperAdmin"))
            {
                // السوبر أدمن يرى كل الموظفين
                usersQuery = usersQuery.Where(u => u.Role == UserRole.Employee);
            }
            else
            {
                // المدراء يرون موظفي شركتهم فقط
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

                if (currentUser?.CompanyId != null)
                {
                    usersQuery = usersQuery.Where(u => u.CompanyId == currentUser.CompanyId && u.Role == UserRole.Employee);
                }
                else
                {
                    // حالة نادرة: مدير بدون شركة
                    usersQuery = usersQuery.Where(u => false);
                }
            }

            var employeesList = await usersQuery
                .Select(u => new {
                    Id = u.Id,
                    Name = u.FullName + " (" + u.JobTitle + ")"
                })
                .OrderBy(u => u.Name)
                .ToListAsync();

            ViewBag.Employees = new SelectList(employeesList, "Id", "Name", selectedEmployee);
        }
    }
}   