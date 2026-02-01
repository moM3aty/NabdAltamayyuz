using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Data;
using NabdAltamayyuz.Models;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;

namespace NabdAltamayyuz.Controllers
{
    [Authorize(Roles = "SuperAdmin,CompanyAdmin,SubAdmin")]
    public class CompaniesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public CompaniesController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: Companies/Index
        public async Task<IActionResult> Index()
        {
            if (User.IsInRole("SuperAdmin"))
            {
                return View(await _context.Companies.Include(c => c.SubCompanies).ToListAsync());
            }
            else
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);

                var subCompanies = await _context.Companies
                    .Where(c => c.ParentCompanyId == user.CompanyId)
                    .ToListAsync();

                return View(subCompanies);
            }
        }

        // GET: Companies/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var company = await _context.Companies
                .Include(c => c.SubCompanies)
                .Include(c => c.Employees)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (company == null) return NotFound();

            if (!User.IsInRole("SuperAdmin"))
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);
                if (company.Id != user.CompanyId && company.ParentCompanyId != user.CompanyId)
                {
                    return Forbid();
                }
            }

            return View(company);
        }

        // GET: Companies/Create
        [Authorize(Roles = "SuperAdmin")]
        public IActionResult Create()
        {
            return View();
        }

            
       [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> Create(Company company, IFormFile? attachment)
        {
            company.CalculateTotal();


            ModelState.Remove("Employees");
            ModelState.Remove("SubCompanies");
            if (ModelState.IsValid)
            {
                // 1. إضافة الشركة
                await HandleAttachment(company, attachment);
                _context.Add(company);
                await _context.SaveChangesAsync(); // حفظ الشركة للحصول على ID

                // 2. إنشاء حساب مستخدم (مدير للشركة) تلقائياً
                if (!string.IsNullOrEmpty(company.Email))
                {
                    var adminUser = new ApplicationUser
                    {
                        FullName = company.ResponsiblePerson ?? "مدير النظام",
                        Email = company.Email, // استخدام بريد الشركة للدخول
                        PasswordHash = "123456", // كلمة المرور الافتراضية
                        Role = UserRole.CompanyAdmin,
                        JobTitle = "المدير العام",
                        PhoneNumber = company.PhoneNumber,
                        NationalId = "-", // قيمة افتراضية
                        CreatedAt = DateTime.Now,
                        CompanyId = company.Id, // ربط المستخدم بالشركة الجديدة
                        Status = "Active",
                        IsSuspended = false
                    };

                    // التحقق من عدم وجود البريد مسبقاً في جدول المستخدمين
                    if (!await _context.Users.AnyAsync(u => u.Email == adminUser.Email))
                    {
                        _context.Users.Add(adminUser);
                        await _context.SaveChangesAsync();
                    }
                }

                TempData["Success"] = "تم إضافة الشركة وإنشاء حساب المدير بنجاح (كلمة المرور: 123456)";
                return RedirectToAction("Index", "Dashboard");
            }
            return View(company);
        }

        // GET: Companies/CreateSub
        [Authorize(Roles = "CompanyAdmin")]
        public async Task<IActionResult> CreateSub()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var user = await _context.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Id == userId);

            if (user?.Company == null) return NotFound();

            var currentSubCount = await _context.Companies.CountAsync(c => c.ParentCompanyId == user.CompanyId);
            if (currentSubCount >= user.Company.AllowedSubAccounts)
            {
                TempData["Error"] = "عذراً، لقد استنفذت عدد الحسابات الفرعية المسموحة.";
                return RedirectToAction("Index");
            }

            return View();
        }

        // POST: Companies/CreateSub
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "CompanyAdmin")]
        public async Task<IActionResult> CreateSub(Company company, IFormFile? attachment)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var user = await _context.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Id == userId);

            company.ParentCompanyId = user.CompanyId;
            company.SubscriptionStartDate = DateTime.Now;
            company.SubscriptionEndDate = user.Company.SubscriptionEndDate;
            company.AllowedSubAccounts = 0;

            if (string.IsNullOrEmpty(company.NationalAddressShortCode)) company.NationalAddressShortCode = "-";
            if (string.IsNullOrEmpty(company.PhoneNumber)) company.PhoneNumber = "-";

            ModelState.Remove("ParentCompany");

            if (ModelState.IsValid)
            {
                await HandleAttachment(company, attachment);
                _context.Add(company);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم إضافة الفرع بنجاح";
                return RedirectToAction(nameof(Index));
            }
            return View(company);
        }

        // GET: Companies/Edit/5
        [Authorize(Roles = "SuperAdmin,CompanyAdmin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var company = await _context.Companies.FindAsync(id);
            if (company == null) return NotFound();

            if (!User.IsInRole("SuperAdmin"))
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);
                // يسمح للمشرف بتعديل الشركات الفرعية التابعة له أو شركته (حسب متطلباتك، هنا نقتصر على الفرعية أو الشركة نفسها)
                if (company.Id != user.CompanyId && company.ParentCompanyId != user.CompanyId) return Forbid();
            }

            return View(company);
        }

        // POST: Companies/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,CompanyAdmin")]
        public async Task<IActionResult> Edit(int id, Company company, IFormFile? attachment)
        {
            if (id != company.Id) return NotFound();

            var existingCompany = await _context.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (existingCompany == null) return NotFound();

            // الحفاظ على القيم الثابتة
            company.CreatedAt = existingCompany.CreatedAt;
            company.ParentCompanyId = existingCompany.ParentCompanyId;
            company.IsSuspended = existingCompany.IsSuspended; // الحالة يتم تغييرها من زر التعليق فقط

            // التعامل مع المرفق
            if (string.IsNullOrEmpty(company.AttachmentPath))
            {
                company.AttachmentPath = existingCompany.AttachmentPath;
            }

            // إعادة حساب الإجمالي إذا تغير السعر
            company.CalculateTotal();

            // تنظيف الـ ModelState
            ModelState.Remove(nameof(company.AttachmentPath));
            ModelState.Remove("Employees");
            ModelState.Remove("SubCompanies");
            if (ModelState.IsValid)
            {
                try
                {
                    if (attachment != null && attachment.Length > 0)
                    {
                        await HandleAttachment(company, attachment);
                    }

                    _context.Update(company);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "تم تحديث بيانات الشركة بنجاح";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Companies.AnyAsync(e => e.Id == id)) return NotFound();
                    else throw;
                }

                if (User.IsInRole("SuperAdmin")) return RedirectToAction("Details", new { id = company.Id });
                return RedirectToAction("Index");
            }
            return View(company);
        }

        private async Task HandleAttachment(Company company, IFormFile? attachment)
        {
            if (attachment != null && attachment.Length > 0)
            {
                string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads/companies");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                string uniqueFileName = Guid.NewGuid().ToString() + "_" + attachment.FileName;
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await attachment.CopyToAsync(fileStream);
                }
                company.AttachmentPath = uniqueFileName;
            }
        }

        // POST: Suspend (تم تحديث الصلاحيات وإصلاح المنطق)
        [HttpPost]
        [Authorize(Roles = "SuperAdmin,CompanyAdmin")]
        public async Task<IActionResult> Suspend(int id)
        {
            var company = await _context.Companies.FindAsync(id);
            if (company == null) return NotFound();

            // التحقق من الصلاحية: هل يحق للمستخدم تعليق هذه الشركة؟
            if (!User.IsInRole("SuperAdmin"))
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);

                // يسمح للمشرف بتعليق الشركات الفرعية التابعة له فقط
                if (company.ParentCompanyId != user.CompanyId)
                {
                    return Forbid();
                }
            }

            company.IsSuspended = !company.IsSuspended;
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = company.IsSuspended ? "تم تعليق الحساب" : "تم تفعيل الحساب" });
        }

        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var company = await _context.Companies.FindAsync(id);
            if (company != null)
            {
                _context.Companies.Remove(company);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم حذف الشركة";
            }
            return RedirectToAction("Index", "Dashboard");
        }
    }
}