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

        // GET: Employees
        public async Task<IActionResult> Index(string searchString, int? companyId, string statusFilter)
        {
            var query = _context.Users.Include(u => u.Company).AsQueryable();

            // 1. تحديد الصلاحيات والنطاق
            if (User.IsInRole("SuperAdmin"))
            {
                // السوبر أدمن يرى الجميع، ويمكنه الفلترة بالشركة
                if (companyId.HasValue)
                {
                    // جلب الشركة والشركات الفرعية التابعة لها إذا كانت شركة رئيسية
                    var subCompanyIds = await _context.Companies
                        .Where(c => c.ParentCompanyId == companyId)
                        .Select(c => c.Id)
                        .ToListAsync();

                    subCompanyIds.Add(companyId.Value);

                    query = query.Where(u => subCompanyIds.Contains(u.CompanyId.Value));
                }

                ViewBag.Companies = new SelectList(await _context.Companies.Where(c => c.ParentCompanyId == null).ToListAsync(), "Id", "Name", companyId);
            }
            else
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(currentUserId);

                // المشرف يرى شركته وفروعها
                if (currentUser?.CompanyId != null)
                {
                    var subCompanyIds = await _context.Companies
                        .Where(c => c.ParentCompanyId == currentUser.CompanyId)
                        .Select(c => c.Id)
                        .ToListAsync();

                    subCompanyIds.Add(currentUser.CompanyId.Value);

                    // إذا تم تحديد شركة معينة في البحث (فرع مثلاً)
                    if (companyId.HasValue && subCompanyIds.Contains(companyId.Value))
                    {
                        query = query.Where(u => u.CompanyId == companyId.Value);
                    }
                    else
                    {
                        query = query.Where(u => subCompanyIds.Contains(u.CompanyId.Value));
                    }

                    // قائمة الشركات للفلتر (الرئيسية والفروع)
                    var myCompanies = await _context.Companies
                        .Where(c => subCompanyIds.Contains(c.Id))
                        .Select(c => new { c.Id, Name = c.ParentCompanyId == null ? c.Name : " -- " + c.Name })
                        .ToListAsync();

                    ViewBag.Companies = new SelectList(myCompanies, "Id", "Name", companyId);
                }
            }

            // 2. البحث
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(u => u.FullName.Contains(searchString) || u.NationalId.Contains(searchString) || u.Email.Contains(searchString) || u.PhoneNumber.Contains(searchString));
            }

            // 3. فلترة الحالة
            if (!string.IsNullOrEmpty(statusFilter))
            {
                if (statusFilter == "Suspended")
                    query = query.Where(u => u.IsSuspended);
                else
                    query = query.Where(u => u.Status == statusFilter && !u.IsSuspended);
            }

            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentStatus = statusFilter;
            ViewBag.CurrentCompany = companyId;

            // استثناء السوبر أدمن من القائمة للمشرفين
            query = query.Where(u => u.Role != UserRole.SuperAdmin);

            return View(await query.OrderBy(u => u.Role).ThenBy(u => u.FullName).ToListAsync());
        }

        // GET: Employees/Details/5
        public async Task<IActionResult> Details(int? id, DateTime? attendanceFrom, DateTime? attendanceTo, string taskStatus)
        {
            if (id == null) return NotFound();

            var employee = await _context.Users
                .Include(u => u.Company)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (employee == null) return NotFound();

            // التحقق من الصلاحية
            if (!User.IsInRole("SuperAdmin"))
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(currentUserId);

                // السماح برؤية موظفي الشركة الرئيسية والفروع
                var allowedCompanyIds = await _context.Companies
                    .Where(c => c.Id == currentUser.CompanyId || c.ParentCompanyId == currentUser.CompanyId)
                    .Select(c => c.Id)
                    .ToListAsync();

                if (!allowedCompanyIds.Contains(employee.CompanyId.Value)) return Forbid();
            }

            // سجل الحضور
            var attendanceQuery = _context.Attendances.Where(a => a.EmployeeId == id).AsQueryable();
            if (attendanceFrom.HasValue) attendanceQuery = attendanceQuery.Where(a => a.Date >= attendanceFrom.Value);
            if (attendanceTo.HasValue) attendanceQuery = attendanceQuery.Where(a => a.Date <= attendanceTo.Value);

            ViewBag.AttendanceLog = await attendanceQuery.OrderByDescending(a => a.Date).ToListAsync();
            ViewBag.AttFrom = attendanceFrom?.ToString("yyyy-MM-dd");
            ViewBag.AttTo = attendanceTo?.ToString("yyyy-MM-dd");

            // سجل المهام
            var tasksQuery = _context.WorkTasks.Where(t => t.AssignedToId == id).AsQueryable();
            if (!string.IsNullOrEmpty(taskStatus) && Enum.TryParse(typeof(NabdAltamayyuz.Models.TaskStatus), taskStatus, out object statusVal))
            {
                tasksQuery = tasksQuery.Where(t => t.Status == (NabdAltamayyuz.Models.TaskStatus)statusVal);
            }

            var tasks = await tasksQuery.OrderByDescending(t => t.DueDate).ToListAsync();
            ViewBag.TasksLog = tasks;

            // حساب نسب الإنجاز
            int totalTasks = tasks.Count;
            int completedTasks = tasks.Count(t => t.IsCompleted);
            double completionRate = totalTasks > 0 ? (double)completedTasks / totalTasks * 100 : 0;

            ViewBag.CompletionRate = Math.Round(completionRate, 1);
            ViewBag.TotalTasks = totalTasks;
            ViewBag.CompletedTasks = completedTasks;
            ViewBag.LateTasks = tasks.Count(t => !t.IsCompleted && t.DueDate < DateTime.Today);

            return View(employee);
        }

        // GET: Employees/Create
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

                ViewBag.Companies = new SelectList(myCompanies, "Id", "Name", user.CompanyId);
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ApplicationUser model, IFormFile? attachment, UserRole? selectedRole)
        {
            ModelState.Clear();

            // التحقق اليدوي
            if (string.IsNullOrWhiteSpace(model.FullName)) ModelState.AddModelError("FullName", "الاسم الكامل مطلوب");
            if (string.IsNullOrWhiteSpace(model.NationalId)) ModelState.AddModelError("NationalId", "رقم الهوية مطلوب");
            if (string.IsNullOrWhiteSpace(model.Email)) ModelState.AddModelError("Email", "البريد الإلكتروني مطلوب");
            if (string.IsNullOrWhiteSpace(model.PhoneNumber)) ModelState.AddModelError("PhoneNumber", "رقم الجوال مطلوب");
            if (string.IsNullOrWhiteSpace(model.JobTitle)) ModelState.AddModelError("JobTitle", "المسمى الوظيفي مطلوب");

            model.PasswordHash = "123456";
            model.CreatedAt = DateTime.Now;
            model.Status = "Active";
            model.IsSuspended = false;

            // تحديد الشركة
            if (User.IsInRole("SuperAdmin"))
            {
                if (model.CompanyId == null) ModelState.AddModelError("CompanyId", "يجب اختيار الشركة");
                model.Role = selectedRole ?? UserRole.Employee;
            }
            else
            {
                // للمشرف: يمكنه إضافة موظف لشركته أو أحد فروعها
                if (model.CompanyId == null)
                {
                    var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                    var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                    model.CompanyId = user.CompanyId;
                }

                // التحقق من أن الشركة المختارة تابعة للمشرف
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);

                var isAllowed = await _context.Companies.AnyAsync(c => c.Id == model.CompanyId && (c.Id == currentUser.CompanyId || c.ParentCompanyId == currentUser.CompanyId));
                if (!isAllowed) return Forbid();

                if (User.IsInRole("CompanyAdmin"))
                    model.Role = (selectedRole == UserRole.SubAdmin) ? UserRole.SubAdmin : UserRole.Employee;
                else
                    model.Role = UserRole.Employee;
            }

            // التحقق من الحدود (Employees Limit)
            if (ModelState.IsValid && model.CompanyId != null && model.Role == UserRole.Employee)
            {
                var company = await _context.Companies.FindAsync(model.CompanyId);
                // إذا كان فرع، تحقق من حدوده الخاصة إن وجدت، أو من الرئيسي
                // المنطق الحالي: كل شركة (رئيسية أو فرعية) لها حدها الخاص المسجل في الجدول
                var currentCount = await _context.Users.CountAsync(u => u.CompanyId == model.CompanyId && u.Role == UserRole.Employee);

                if (currentCount >= company.AllowedEmployees)
                {
                    ModelState.AddModelError("", $"عذراً، تم تجاوز الحد المسموح للموظفين في هذه الشركة/الفرع ({company.AllowedEmployees}).");
                }

                if (await _context.Users.AnyAsync(u => u.Email == model.Email))
                    ModelState.AddModelError("Email", "البريد الإلكتروني مسجل مسبقاً.");
            }

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
                    ModelState.AddModelError("", "خطأ في الحفظ: " + ex.Message);
                }
            }

            // إعادة ملء القوائم عند الخطأ
            if (User.IsInRole("SuperAdmin"))
            {
                ViewBag.Companies = new SelectList(await _context.Companies.ToListAsync(), "Id", "Name", model.CompanyId);
            }
            else
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);
                var myCompanies = await _context.Companies
                    .Where(c => c.Id == user.CompanyId || c.ParentCompanyId == user.CompanyId)
                    .Select(c => new { c.Id, Name = c.ParentCompanyId == null ? c.Name : " -- " + c.Name })
                    .ToListAsync();
                ViewBag.Companies = new SelectList(myCompanies, "Id", "Name", model.CompanyId);
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ApplicationUser model, IFormFile? attachment, string? newPassword)
        {
            if (id != model.Id) return NotFound();
            var userToUpdate = await _context.Users.FindAsync(id);
            if (userToUpdate == null) return NotFound();

            userToUpdate.FullName = model.FullName;
            userToUpdate.NationalId = model.NationalId;
            userToUpdate.PhoneNumber = model.PhoneNumber;
            userToUpdate.JobTitle = model.JobTitle;
            userToUpdate.Status = model.Status;

            if (!string.IsNullOrEmpty(newPassword)) userToUpdate.PasswordHash = newPassword;

            if (attachment != null && attachment.Length > 0)
            {
                // حفظ المرفق (نفس المنطق السابق)
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
            TempData["Success"] = "تم تحديث البيانات بنجاح";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Suspend(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            user.IsSuspended = !user.IsSuspended;
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = user.IsSuspended ? "تم تعليق الحساب" : "تم تفعيل الحساب" });
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            user.PasswordHash = "123456";
            await _context.SaveChangesAsync();
            TempData["Success"] = "تم إعادة تعيين كلمة المرور";
            return RedirectToAction("Edit", new { id = id });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var employee = await _context.Users.FindAsync(id);
            if (employee != null)
            {
                var tasks = _context.WorkTasks.Where(t => t.AssignedToId == id || t.CreatedById == id);
                _context.WorkTasks.RemoveRange(tasks);
                var attendances = _context.Attendances.Where(a => a.EmployeeId == id);
                _context.Attendances.RemoveRange(attendances);
                _context.Users.Remove(employee);
                await _context.SaveChangesAsync();
            }
            TempData["Success"] = "تم الحذف بنجاح";
            return RedirectToAction(nameof(Index));
        }
    }
}