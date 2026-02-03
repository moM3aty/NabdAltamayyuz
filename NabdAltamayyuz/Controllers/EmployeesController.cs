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

namespace NabdAltamayyuz.Controllers
{
    [Authorize(Roles = "CompanyAdmin,SubAdmin,SuperAdmin")]
    public class EmployeesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public EmployeesController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: Employees (تم التحديث لدعم البحث والفلترة)
        public async Task<IActionResult> Index(string searchString, int? companyId, string statusFilter)
        {
            var query = _context.Users.Include(u => u.Company).AsQueryable();

            // 1. تحديد الصلاحيات (النطاق)
            if (User.IsInRole("SuperAdmin"))
            {
                if (companyId.HasValue) query = query.Where(u => u.CompanyId == companyId);
            }
            else
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(currentUserId);
                if (currentUser?.CompanyId != null) query = query.Where(u => u.CompanyId == currentUser.CompanyId);
            }

            // 2. البحث بالاسم أو الهوية
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(u => u.FullName.Contains(searchString) || u.NationalId.Contains(searchString));
            }

            // 3. فلترة الحالة
            if (!string.IsNullOrEmpty(statusFilter))
            {
                if (statusFilter == "Suspended")
                    query = query.Where(u => u.IsSuspended);
                else
                    query = query.Where(u => u.Status == statusFilter && !u.IsSuspended);
            }

            // تجهيز القوائم للـ View (للمالك)
            if (User.IsInRole("SuperAdmin"))
            {
                ViewBag.Companies = new SelectList(await _context.Companies.ToListAsync(), "Id", "Name", companyId);
            }

            // حفظ قيم البحث الحالية
            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentStatus = statusFilter;

            return View(await query.OrderBy(u => u.Role).ThenBy(u => u.FullName).ToListAsync());
        }

        // GET: Employees/Details/5 (تم التحديث لعرض السجلات)
        public async Task<IActionResult> Details(int? id, DateTime? attendanceFrom, DateTime? attendanceTo, string taskStatus)
        {
            if (id == null) return NotFound();

            var employee = await _context.Users
                .Include(u => u.Company)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (employee == null) return NotFound();

            if (!User.IsInRole("SuperAdmin"))
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(currentUserId);
                if (employee.CompanyId != currentUser.CompanyId) return Forbid();
            }

            // جلب سجل الحضور
            var attendanceQuery = _context.Attendances.Where(a => a.EmployeeId == id).AsQueryable();
            if (attendanceFrom.HasValue) attendanceQuery = attendanceQuery.Where(a => a.Date >= attendanceFrom.Value);
            if (attendanceTo.HasValue) attendanceQuery = attendanceQuery.Where(a => a.Date <= attendanceTo.Value);

            ViewBag.AttendanceLog = await attendanceQuery.OrderByDescending(a => a.Date).ToListAsync();
            ViewBag.AttFrom = attendanceFrom?.ToString("yyyy-MM-dd");
            ViewBag.AttTo = attendanceTo?.ToString("yyyy-MM-dd");

            // جلب سجل المهام
            var tasksQuery = _context.WorkTasks.Where(t => t.AssignedToId == id).AsQueryable();
            if (!string.IsNullOrEmpty(taskStatus) && Enum.TryParse(typeof(NabdAltamayyuz.Models.TaskStatus), taskStatus, out object statusVal))
            {
                tasksQuery = tasksQuery.Where(t => t.Status == (NabdAltamayyuz.Models.TaskStatus)statusVal);
            }

            ViewBag.TasksLog = await tasksQuery.OrderByDescending(t => t.DueDate).ToListAsync();

            return View(employee);
        }

        // GET: Employees/Create
        public async Task<IActionResult> Create()
        {
            if (User.IsInRole("SuperAdmin"))
            {
                ViewBag.Companies = new SelectList(await _context.Companies.ToListAsync(), "Id", "Name");
            }
            return View();
        }

        // POST: Employees/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ApplicationUser model, IFormFile? attachment, UserRole? selectedRole)
        {
            // 1. تنظيف الـ ModelState (لحل مشكلة عدم الإضافة)
            ModelState.Clear();

            // 2. التحقق اليدوي
            if (string.IsNullOrWhiteSpace(model.FullName)) ModelState.AddModelError("FullName", "الاسم الكامل مطلوب");
            if (string.IsNullOrWhiteSpace(model.NationalId)) ModelState.AddModelError("NationalId", "رقم الهوية مطلوب");
            if (string.IsNullOrWhiteSpace(model.Email)) ModelState.AddModelError("Email", "البريد الإلكتروني مطلوب");
            if (string.IsNullOrWhiteSpace(model.PhoneNumber)) ModelState.AddModelError("PhoneNumber", "رقم الجوال مطلوب");
            if (string.IsNullOrWhiteSpace(model.JobTitle)) ModelState.AddModelError("JobTitle", "المسمى الوظيفي مطلوب");

            // 3. إعداد البيانات الافتراضية
            model.PasswordHash = "123456";
            model.CreatedAt = DateTime.Now;
            model.Status = "Active";
            model.IsSuspended = false;

            int? targetCompanyId = null;

            // 4. تحديد الشركة
            if (User.IsInRole("SuperAdmin"))
            {
                targetCompanyId = model.CompanyId;
                if (targetCompanyId == null) ModelState.AddModelError("CompanyId", "يجب اختيار الشركة");
                model.Role = selectedRole ?? UserRole.Employee;
            }
            else
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);

                targetCompanyId = currentUser?.CompanyId;
                model.CompanyId = targetCompanyId;

                if (targetCompanyId == null)
                    ModelState.AddModelError("", "خطأ: حسابك غير مرتبط بشركة.");

                if (User.IsInRole("CompanyAdmin"))
                    model.Role = (selectedRole == UserRole.SubAdmin) ? UserRole.SubAdmin : UserRole.Employee;
                else
                    model.Role = UserRole.Employee;
            }

            // 5. التحقق من القواعد (تكرار + كوتا)
            if (ModelState.IsValid && targetCompanyId != null)
            {
                if (await _context.Users.AnyAsync(u => u.Email == model.Email))
                    ModelState.AddModelError("Email", "البريد الإلكتروني مسجل مسبقاً.");

                if (model.Role == UserRole.Employee)
                {
                    var company = await _context.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == targetCompanyId);
                    if (company != null && company.AllowedEmployees > 0)
                    {
                        var currentCount = await _context.Users.CountAsync(u => u.CompanyId == targetCompanyId && u.Role == UserRole.Employee);
                        if (currentCount >= company.AllowedEmployees)
                            ModelState.AddModelError("", $"عذراً، تجاوزت الحد المسموح ({company.AllowedEmployees} موظف).");
                    }
                }
            }

            // 6. الحفظ
            if (ModelState.IsValid)
            {
                try
                {
                    if (attachment != null && attachment.Length > 0)
                    {
                        string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads/employees");
                        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                        string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(attachment.FileName);
                        string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await attachment.CopyToAsync(fileStream);
                        }
                        model.AttachmentPath = uniqueFileName;
                    }

                    _context.Add(model);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = "تم إضافة الموظف بنجاح";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"خطأ قاعدة البيانات: {ex.Message}");
                }
            }

            if (User.IsInRole("SuperAdmin"))
            {
                ViewBag.Companies = new SelectList(await _context.Companies.ToListAsync(), "Id", "Name", model.CompanyId);
            }
            return View(model);
        }

        // GET: Employees/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var employee = await _context.Users.FindAsync(id);
            if (employee == null) return NotFound();
            return View(employee);
        }

        // POST: Employees/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ApplicationUser model, IFormFile? attachment)
        {
            if (id != model.Id) return NotFound();

            var userToUpdate = await _context.Users.FindAsync(id);
            if (userToUpdate == null) return NotFound();

            ModelState.Clear();
            if (string.IsNullOrEmpty(model.FullName)) ModelState.AddModelError("FullName", "الاسم مطلوب");
            if (string.IsNullOrEmpty(model.NationalId)) ModelState.AddModelError("NationalId", "رقم الهوية مطلوب");

            if (ModelState.IsValid)
            {
                userToUpdate.FullName = model.FullName;
                userToUpdate.NationalId = model.NationalId;
                userToUpdate.JobTitle = model.JobTitle;
                userToUpdate.PhoneNumber = model.PhoneNumber;
                userToUpdate.Status = model.Status;

                if (attachment != null && attachment.Length > 0)
                {
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads/employees");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(attachment.FileName);
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await attachment.CopyToAsync(fileStream);
                    }
                    userToUpdate.AttachmentPath = uniqueFileName;
                }

                _context.Update(userToUpdate);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم التحديث بنجاح";
                return RedirectToAction(nameof(Index));
            }
            return View(model);
        }

        // POST: Employees/Suspend/5
        [HttpPost]
        public async Task<IActionResult> Suspend(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            // التحقق من الصلاحية
            if (!User.IsInRole("SuperAdmin"))
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(currentUserId);
                if (user.CompanyId != currentUser.CompanyId) return Forbid();
            }

            user.IsSuspended = !user.IsSuspended;
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = user.IsSuspended ? "تم تعليق الحساب" : "تم تفعيل الحساب" });
        }

        // POST: Employees/ResetPassword/5
        [HttpPost]
        public async Task<IActionResult> ResetPassword(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            if (!User.IsInRole("SuperAdmin"))
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(currentUserId);
                if (user.CompanyId != currentUser.CompanyId) return Forbid();
            }

            user.PasswordHash = "123456";
            await _context.SaveChangesAsync();
            TempData["Success"] = "تم إعادة تعيين كلمة المرور إلى 123456";

            return RedirectToAction("Edit", new { id = id });
        }

        // POST: Employees/Delete/5
        public async Task<IActionResult> Delete(int id)
        {
            var employee = await _context.Users.FindAsync(id);
            if (employee != null)
            {
                // حذف المتعلقات
                var tasks = _context.WorkTasks.Where(t => t.AssignedToId == id || t.CreatedById == id);
                _context.WorkTasks.RemoveRange(tasks);
                var attendances = _context.Attendances.Where(a => a.EmployeeId == id);
                _context.Attendances.RemoveRange(attendances);

                _context.Users.Remove(employee);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم الحذف بنجاح";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}