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
        public async Task<IActionResult> Index(int? companyId)
        {
            var query = _context.Users.AsQueryable();

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

            return View(await query.OrderBy(u => u.Role).ThenBy(u => u.FullName).ToListAsync());
        }

        // GET: Employees/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var employee = await _context.Users.Include(u => u.Company).FirstOrDefaultAsync(m => m.Id == id);
            if (employee == null) return NotFound();

            if (!User.IsInRole("SuperAdmin"))
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(currentUserId);
                if (employee.CompanyId != currentUser.CompanyId) return Forbid();
            }
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
            // 1. تنظيف الـ ModelState من الحقول التي لا يدخلها المستخدم
            ModelState.Remove(nameof(model.Id));
            ModelState.Remove(nameof(model.PasswordHash));
            ModelState.Remove(nameof(model.Company));
            ModelState.Remove(nameof(model.Role));
            ModelState.Remove(nameof(model.AttachmentPath));
            ModelState.Remove(nameof(model.Status));
            ModelState.Remove(nameof(model.CreatedAt));
            ModelState.Remove(nameof(model.IsSuspended));

            if (!User.IsInRole("SuperAdmin"))
            {
                ModelState.Remove(nameof(model.CompanyId));
            }

            // 2. إعداد البيانات الافتراضية
            model.PasswordHash = "123456";
            model.CreatedAt = DateTime.Now;
            model.Status = "Active";
            model.IsSuspended = false;

            int? targetCompanyId = null;

            // 3. تحديد الشركة والصلاحيات
            if (User.IsInRole("SuperAdmin"))
            {
                targetCompanyId = model.CompanyId;
                model.Role = selectedRole ?? UserRole.Employee;
            }
            else
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);

                targetCompanyId = currentUser?.CompanyId;
                model.CompanyId = targetCompanyId;

                if (User.IsInRole("CompanyAdmin"))
                    model.Role = (selectedRole == UserRole.SubAdmin) ? UserRole.SubAdmin : UserRole.Employee;
                else
                    model.Role = UserRole.Employee;
            }

            // 4. التحقق من القواعد (Business Rules)

            if (targetCompanyId == null && !User.IsInRole("SuperAdmin"))
            {
                ModelState.AddModelError("", "خطأ: لم يتم العثور على بيانات الشركة للمستخدم الحالي.");
                return ReLoadView(model);
            }

            // التحقق من الحصة (Quota)
            if (targetCompanyId != null && model.Role == UserRole.Employee)
            {
                var company = await _context.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == targetCompanyId);
                if (company != null)
                {
                    var currentCount = await _context.Users.CountAsync(u => u.CompanyId == targetCompanyId && u.Role == UserRole.Employee);

                    // التعديل: إذا كان العدد المسموح أكبر من 0، نتحقق من الحد. أما إذا كان 0، نعتبره غير محدود (أو يمكنك إجبار المستخدم على تحديث بيانات الشركة)
                    if (company.AllowedEmployees > 0 && currentCount >= company.AllowedEmployees)
                    {
                        ModelState.AddModelError("", $"عذراً، لقد تجاوزت عدد الموظفين المسموح به ({company.AllowedEmployees}).");
                        return ReLoadView(model);
                    }
                }
            }

            // التحقق من تكرار البريد
            if (await _context.Users.AnyAsync(u => u.Email == model.Email))
            {
                ModelState.AddModelError("Email", "البريد الإلكتروني مسجل مسبقاً.");
                return ReLoadView(model);
            }

            // 5. الحفظ النهائي
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
                    ModelState.AddModelError("", $"حدث خطأ في قاعدة البيانات: {ex.Message}");
                }
            }

            return ReLoadView(model);
        }

        private IActionResult ReLoadView(ApplicationUser model)
        {
            if (User.IsInRole("SuperAdmin"))
            {
                ViewBag.Companies = new SelectList(_context.Companies.ToList(), "Id", "Name", model.CompanyId);
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

        // POST: Employees/Delete/5
        public async Task<IActionResult> Delete(int id)
        {
            var employee = await _context.Users.FindAsync(id);
            if (employee != null)
            {
                _context.Users.Remove(employee);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم الحذف بنجاح";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}