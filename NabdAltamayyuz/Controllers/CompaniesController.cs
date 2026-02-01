using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Data;
using NabdAltamayyuz.Models;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;
using System;

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

        // POST: Companies/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> Create(Company company, IFormFile? attachment, string? adminPassword)
        {
            company.CalculateTotal();

            if (string.IsNullOrEmpty(company.NationalAddressShortCode)) company.NationalAddressShortCode = "-";
            if (string.IsNullOrEmpty(company.PhoneNumber)) company.PhoneNumber = "-";
            ModelState.Remove(nameof(company.Employees));
            ModelState.Remove(nameof(company.SubCompanies));
            if (ModelState.IsValid)
            {
                await HandleAttachment(company, attachment);
                _context.Add(company);
                await _context.SaveChangesAsync();

                if (!string.IsNullOrEmpty(company.Email))
                {
                    string passwordToUse = !string.IsNullOrEmpty(adminPassword) ? adminPassword : "123456";

                    var adminUser = new ApplicationUser
                    {
                        FullName = company.ResponsiblePerson ?? "مدير النظام",
                        Email = company.Email,
                        PasswordHash = passwordToUse,
                        Role = UserRole.CompanyAdmin,
                        JobTitle = "المدير العام",
                        PhoneNumber = company.PhoneNumber,
                        NationalId = "-",
                        CreatedAt = DateTime.Now,
                        CompanyId = company.Id,
                        Status = "Active",
                        IsSuspended = false
                    };

                    if (!await _context.Users.AnyAsync(u => u.Email == adminUser.Email))
                    {
                        _context.Users.Add(adminUser);
                        await _context.SaveChangesAsync();
                    }
                }

                TempData["Success"] = "تم إضافة الشركة وإنشاء حساب المدير بنجاح";
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
        public async Task<IActionResult> CreateSub(Company company, IFormFile? attachment, string? adminPassword)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var user = await _context.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Id == userId);

            company.ParentCompanyId = user.CompanyId;
            company.SubscriptionStartDate = DateTime.Now;
            company.SubscriptionEndDate = user.Company.SubscriptionEndDate;
            company.AllowedSubAccounts = 0;

            if (string.IsNullOrEmpty(company.NationalAddressShortCode)) company.NationalAddressShortCode = "-";
            if (string.IsNullOrEmpty(company.PhoneNumber)) company.PhoneNumber = "-";
            if (string.IsNullOrEmpty(company.TaxNumber)) company.TaxNumber = "-"; 
            if (string.IsNullOrEmpty(company.UnifiedNumber)) company.UnifiedNumber = "-";

            ModelState.Remove("ParentCompany");
            ModelState.Remove("Employees");
            ModelState.Remove("TaxNumber");
            ModelState.Remove("SubCompanies");
            ModelState.Remove("UnifiedNumber");
            ModelState.Remove("NationalAddressShortCode");

            if (ModelState.IsValid)
            {
                await HandleAttachment(company, attachment);
                _context.Add(company);
                await _context.SaveChangesAsync();

                if (!string.IsNullOrEmpty(company.Email))
                {
                    string passwordToUse = !string.IsNullOrEmpty(adminPassword) ? adminPassword : "123456";

                    var subAdminUser = new ApplicationUser
                    {
                        FullName = company.ResponsiblePerson ?? "مدير الفرع",
                        Email = company.Email,
                        PasswordHash = passwordToUse,
                        Role = UserRole.SubAdmin,
                        JobTitle = "مدير فرع",
                        PhoneNumber = company.PhoneNumber,
                        NationalId = "-",
                        CreatedAt = DateTime.Now,
                        CompanyId = company.Id,
                        Status = "Active",
                        IsSuspended = false
                    };

                    if (!await _context.Users.AnyAsync(u => u.Email == subAdminUser.Email))
                    {
                        _context.Users.Add(subAdminUser);
                        await _context.SaveChangesAsync();
                    }
                }

                TempData["Success"] = "تم إضافة الفرع وإنشاء حساب المدير بنجاح";
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
                if (company.Id != user.CompanyId && company.ParentCompanyId != user.CompanyId) return Forbid();
            }

            return View(company);
        }

        // POST: Companies/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,CompanyAdmin")]
        public async Task<IActionResult> Edit(int id, Company company, IFormFile? attachment, string? adminPassword)
        {
            if (id != company.Id) return NotFound();

            var existingCompany = await _context.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (existingCompany == null) return NotFound();

            company.CreatedAt = existingCompany.CreatedAt;
            company.ParentCompanyId = existingCompany.ParentCompanyId;
            company.IsSuspended = existingCompany.IsSuspended;

            if (string.IsNullOrEmpty(company.NationalAddressShortCode))
                company.NationalAddressShortCode = existingCompany.NationalAddressShortCode ?? "-";
            if (string.IsNullOrEmpty(company.PhoneNumber))
                company.PhoneNumber = existingCompany.PhoneNumber ?? "-";

            if (string.IsNullOrEmpty(company.AttachmentPath))
                company.AttachmentPath = existingCompany.AttachmentPath;

            company.CalculateTotal();

            ModelState.Remove(nameof(company.NationalAddressShortCode));
            ModelState.Remove(nameof(company.PhoneNumber));
            ModelState.Remove(nameof(company.AttachmentPath));
            ModelState.Remove(nameof(company.Employees));
            ModelState.Remove(nameof(company.SubCompanies));

            if (ModelState.IsValid)
            {
                try
                {
                    if (attachment != null && attachment.Length > 0)
                    {
                        await HandleAttachment(company, attachment);
                    }

                    // تحديث بيانات الشركة
                    _context.Update(company);

                    // تحديث كلمة مرور المدير إذا تم إدخالها (للمالك فقط)
                    if (!string.IsNullOrEmpty(adminPassword) && User.IsInRole("SuperAdmin"))
                    {
                        // البحث عن مدير هذه الشركة (CompanyAdmin أو SubAdmin)
                        var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.CompanyId == id && (u.Role == UserRole.CompanyAdmin || u.Role == UserRole.SubAdmin));
                        if (adminUser != null)
                        {
                            adminUser.PasswordHash = adminPassword;
                            _context.Update(adminUser);
                        }
                    }

                    await _context.SaveChangesAsync();
                    TempData["Success"] = "تم تحديث بيانات الشركة بنجاح";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Companies.AnyAsync(e => e.Id == id)) return NotFound();
                    else throw;
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "حدث خطأ أثناء الحفظ: " + ex.Message);
                    return View(company);
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

        [HttpPost]
        [Authorize(Roles = "SuperAdmin,CompanyAdmin")]
        public async Task<IActionResult> Suspend(int id)
        {
            var company = await _context.Companies.FindAsync(id);
            if (company == null) return NotFound();

            if (!User.IsInRole("SuperAdmin"))
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FindAsync(userId);
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
            var company = await _context.Companies
                .Include(c => c.Employees)
                .Include(c => c.SubCompanies)
                    .ThenInclude(sc => sc.Employees)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (company != null)
            {
                async Task DeleteCompanyUsers(IEnumerable<ApplicationUser> users)
                {
                    if (users == null || !users.Any()) return;
                    var userIds = users.Select(u => u.Id).ToList();

                    var tasks = _context.WorkTasks.Where(t => userIds.Contains(t.AssignedToId) || userIds.Contains(t.CreatedById));
                    _context.WorkTasks.RemoveRange(tasks);

                    var attendances = _context.Attendances.Where(a => userIds.Contains(a.EmployeeId));
                    _context.Attendances.RemoveRange(attendances);

                    _context.Users.RemoveRange(users);
                    await Task.CompletedTask;
                }

                if (company.SubCompanies != null && company.SubCompanies.Any())
                {
                    foreach (var sub in company.SubCompanies)
                    {
                        await DeleteCompanyUsers(sub.Employees);
                        _context.Companies.Remove(sub);
                    }
                }

                await DeleteCompanyUsers(company.Employees);
                _context.Companies.Remove(company);

                await _context.SaveChangesAsync();
                TempData["Success"] = "تم حذف الشركة وكافة البيانات المرتبطة بنجاح";
            }
            return RedirectToAction("Index", "Dashboard");
        }
    }
}