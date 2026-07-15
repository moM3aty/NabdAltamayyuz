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
            var query = _context.Users
                .Include(u => u.Company)
                .Include(u => u.Project) // جلب المشروع
                .Where(u => u.Role == UserRole.Employee)
                .AsQueryable();

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

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(u => u.FullName.Contains(searchString) || u.NationalId.Contains(searchString));
            }

            if (!string.IsNullOrEmpty(statusFilter))
            {
                if (statusFilter == "Suspended")
                    query = query.Where(u => u.IsSuspended);
                else
                    query = query.Where(u => u.Status == statusFilter && !u.IsSuspended);
            }

            query = query.OrderBy(u => u.FullName);

            if (User.IsInRole("SuperAdmin"))
            {
                ViewBag.Companies = new SelectList(await _context.Companies.ToListAsync(), "Id", "Name", companyId);
            }

            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentStatus = statusFilter;
            ViewBag.CurrentCompany = companyId;

            return View(await query.ToListAsync());
        }

        // GET: Employees/Details/5
        public async Task<IActionResult> Details(int? id, DateTime? attendanceFrom, DateTime? attendanceTo, string taskStatus, string interactionMonth)
        {
            if (id == null) return NotFound();

            var employee = await _context.Users
                .Include(u => u.Company)
                .Include(u => u.Project)
                .Include(u => u.ProjectJobRole)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (employee == null) return NotFound();

            if (!User.IsInRole("SuperAdmin"))
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(currentUserId);
                if (employee.CompanyId != currentUser.CompanyId) return Forbid();
            }

            var attendanceQuery = _context.Attendances.Where(a => a.EmployeeId == id).AsQueryable();
            if (attendanceFrom.HasValue) attendanceQuery = attendanceQuery.Where(a => a.Date >= attendanceFrom.Value);
            if (attendanceTo.HasValue) attendanceQuery = attendanceQuery.Where(a => a.Date <= attendanceTo.Value);
            ViewBag.AttendanceLog = await attendanceQuery.OrderByDescending(a => a.Date).ToListAsync();
            ViewBag.AttFrom = attendanceFrom?.ToString("yyyy-MM-dd");
            ViewBag.AttTo = attendanceTo?.ToString("yyyy-MM-dd");

            var tasksQuery = _context.WorkTasks.Where(t => t.AssignedToId == id).AsQueryable();
            if (!string.IsNullOrEmpty(taskStatus) && Enum.TryParse(typeof(NabdAltamayyuz.Models.TaskStatus), taskStatus, out object statusVal))
            {
                tasksQuery = tasksQuery.Where(t => t.Status == (NabdAltamayyuz.Models.TaskStatus)statusVal);
            }
            ViewBag.TasksLog = await tasksQuery.OrderByDescending(t => t.DueDate).ToListAsync();

            // إحصائيات المهام العامة
            var allTasksForStats = await _context.WorkTasks.Where(t => t.AssignedToId == id).ToListAsync();
            ViewBag.TotalTasks = allTasksForStats.Count;
            ViewBag.CompletedTasks = allTasksForStats.Count(t => t.IsCompleted);
            ViewBag.LateTasks = allTasksForStats.Count(t => !t.IsCompleted && t.DueDate < DateTime.Today);
            ViewBag.CompletionRate = allTasksForStats.Count > 0 ? Math.Round((double)ViewBag.CompletedTasks / allTasksForStats.Count * 100, 1) : 0;

            // --- منطق إنشاء وجلب سجل التفاعل الشهري ---
            DateTime targetMonth;
            if (!string.IsNullOrEmpty(interactionMonth) && DateTime.TryParse(interactionMonth + "-01", out DateTime parsedMonth))
            {
                targetMonth = parsedMonth;
            }
            else
            {
                targetMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            }

            var interaction = await _context.MonthlyInteractions.FirstOrDefaultAsync(m => m.EmployeeId == id && m.MonthYear.Year == targetMonth.Year && m.MonthYear.Month == targetMonth.Month);

            if (interaction == null)
            {
                interaction = new MonthlyInteraction
                {
                    EmployeeId = id.Value,
                    MonthYear = targetMonth,
                    RequiredHours = 176 // المقرر التلقائي
                };
                _context.MonthlyInteractions.Add(interaction);
                await _context.SaveChangesAsync();
            }

            if (!interaction.IsManuallyEdited)
            {
                // حساب ساعات الحضور الفعلية لهذا الشهر
                var monthAttendances = await _context.Attendances
                    .Where(a => a.EmployeeId == id && a.Date.Year == targetMonth.Year && a.Date.Month == targetMonth.Month && a.TimeIn != null && a.TimeOut != null)
                    .ToListAsync();

                double totalHours = monthAttendances.Sum(a => (a.TimeOut.Value - a.TimeIn.Value).TotalHours);
                interaction.CompletedHours = Math.Round(totalHours, 1);

                // حساب المهام المسندة والمنجزة لهذا الشهر
                var monthTasks = await _context.WorkTasks
                    .Where(t => t.AssignedToId == id && t.DueDate.Year == targetMonth.Year && t.DueDate.Month == targetMonth.Month)
                    .ToListAsync();

                interaction.TotalTasks = monthTasks.Count;
                interaction.CompletedTasks = monthTasks.Count(t => t.IsCompleted);

                _context.Update(interaction);
                await _context.SaveChangesAsync();
            }

            ViewBag.MonthlyInteraction = interaction;
            ViewBag.SelectedMonth = targetMonth.ToString("yyyy-MM");

            return View(employee);
        }

        // POST: Update Monthly Interaction Manually
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateInteraction(int interactionId, double requiredHours, double completedHours, string redirectMonth)
        {
            var interaction = await _context.MonthlyInteractions.FindAsync(interactionId);
            if (interaction != null)
            {
                interaction.RequiredHours = requiredHours;
                interaction.CompletedHours = completedHours;
                interaction.IsManuallyEdited = true;
                _context.Update(interaction);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم تحديث بيانات التفاعل الشهري يدوياً.";
            }
            return RedirectToAction(nameof(Details), new { id = interaction?.EmployeeId, interactionMonth = redirectMonth });
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
            ModelState.Clear();

            if (string.IsNullOrWhiteSpace(model.FullName)) ModelState.AddModelError("FullName", "الاسم الكامل مطلوب");
            if (string.IsNullOrWhiteSpace(model.NationalId)) ModelState.AddModelError("NationalId", "رقم الهوية مطلوب");
            if (string.IsNullOrWhiteSpace(model.Email)) ModelState.AddModelError("Email", "البريد الإلكتروني مطلوب");
            if (string.IsNullOrWhiteSpace(model.PhoneNumber)) ModelState.AddModelError("PhoneNumber", "رقم الجوال مطلوب");

            model.PasswordHash = "123456";
            model.CreatedAt = DateTime.Now;
            if (model.HireDate == null || model.HireDate == DateTime.MinValue) model.HireDate = DateTime.Today;
            model.Status = "Active";
            model.IsSuspended = false;

            int? targetCompanyId = null;

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

                model.Role = UserRole.Employee;
            }

            // جلب اسم المسمى الوظيفي بناءً على ما تم اختياره من القائمة المنسدلة (ProjectJobRole)
            if (model.ProjectJobRoleId.HasValue && string.IsNullOrEmpty(model.JobTitle))
            {
                var role = await _context.ProjectJobRoles.FindAsync(model.ProjectJobRoleId.Value);
                if (role != null) model.JobTitle = role.Name;
            }

            if (string.IsNullOrEmpty(model.JobTitle)) ModelState.AddModelError("JobTitle", "المسمى الوظيفي مطلوب");

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
        public async Task<IActionResult> Edit(int id, ApplicationUser model, IFormFile? attachment, string? newPassword)
        {
            if (id != model.Id) return NotFound();

            var userToUpdate = await _context.Users.FindAsync(id);
            if (userToUpdate == null) return NotFound();

            ModelState.Clear();
            if (string.IsNullOrEmpty(model.FullName)) ModelState.AddModelError("FullName", "الاسم مطلوب");
            if (string.IsNullOrEmpty(model.NationalId)) ModelState.AddModelError("NationalId", "رقم الهوية مطلوب");

            if (model.ProjectJobRoleId.HasValue && string.IsNullOrEmpty(model.JobTitle))
            {
                var role = await _context.ProjectJobRoles.FindAsync(model.ProjectJobRoleId.Value);
                if (role != null) model.JobTitle = role.Name;
            }

            if (ModelState.IsValid)
            {
                userToUpdate.FullName = model.FullName;
                userToUpdate.NationalId = model.NationalId;
                userToUpdate.JobTitle = model.JobTitle;
                userToUpdate.PhoneNumber = model.PhoneNumber;
                userToUpdate.Status = model.Status;
                userToUpdate.HireDate = model.HireDate;
                userToUpdate.ProjectId = model.ProjectId;
                userToUpdate.ProjectJobRoleId = model.ProjectJobRoleId;

                if (!string.IsNullOrEmpty(newPassword))
                {
                    userToUpdate.PasswordHash = newPassword;
                }

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
                TempData["Success"] = "تم تحديث البيانات بنجاح";
                return RedirectToAction(nameof(Index));
            }
            return View(model);
        }

        // AJAX: جلب المشاريع لشركة معينة
        [HttpGet]
        public async Task<IActionResult> GetProjectsByCompany(int companyId)
        {
            var projects = await _context.Projects
                .Where(p => p.CompanyId == companyId)
                .Select(p => new { id = p.Id, name = p.Name })
                .ToListAsync();
            return Json(projects);
        }

        // AJAX: جلب المهن لمشروع معين
        [HttpGet]
        public async Task<IActionResult> GetRolesByProject(int projectId)
        {
            var roles = await _context.ProjectJobRoles
                .Where(r => r.ProjectId == projectId)
                .Select(r => new { id = r.Id, name = r.Name })
                .ToListAsync();
            return Json(roles);
        }

        [HttpPost]
        public async Task<IActionResult> Suspend(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

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
                var leaves = _context.LeaveRequests.Where(l => l.EmployeeId == id);
                _context.LeaveRequests.RemoveRange(leaves);
                var interactions = _context.MonthlyInteractions.Where(m => m.EmployeeId == id);
                _context.MonthlyInteractions.RemoveRange(interactions);

                _context.Users.Remove(employee);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم الحذف بنجاح";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}