using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Data;
using NabdAltamayyuz.Models;
using System.Linq;
using System.Threading.Tasks;

namespace NabdAltamayyuz.Controllers
{
    [Authorize(Roles = "CompanyAdmin,SuperAdmin")]
    public class EmployeesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public EmployeesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Employees
        public async Task<IActionResult> Index()
        {
            // Fetch only Employees (Exclude Admins)
            var employees = await _context.Users
                .Where(u => u.Role == UserRole.Employee)
                .ToListAsync();
            return View(employees);
        }

        // GET: Employees/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Employees/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ApplicationUser model)
        {
            // Remove validation for fields we set manually
            ModelState.Remove("PasswordHash");
            ModelState.Remove("Company");

            if (ModelState.IsValid)
            {
                // Check if email exists
                if (await _context.Users.AnyAsync(u => u.Email == model.Email))
                {
                    ModelState.AddModelError("Email", "البريد الإلكتروني مسجل مسبقاً");
                    return View(model);
                }

                // Set Default Values
                model.Role = UserRole.Employee;
                model.PasswordHash = "123456"; // Default password
                model.CreatedAt = System.DateTime.Now;

                _context.Add(model);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم إضافة الموظف بنجاح";
                return RedirectToAction(nameof(Index));
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
        public async Task<IActionResult> Edit(int id, ApplicationUser model)
        {
            if (id != model.Id) return NotFound();

            // We only want to update specific fields
            var userToUpdate = await _context.Users.FindAsync(id);
            if (userToUpdate == null) return NotFound();

            // Manually update allowed fields
            userToUpdate.FullName = model.FullName;
            userToUpdate.JobTitle = model.JobTitle;
            userToUpdate.PhoneNumber = model.PhoneNumber;
            // Note: Email and Password should be handled separately for security

            try
            {
                _context.Update(userToUpdate);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم تحديث بيانات الموظف";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Users.AnyAsync(e => e.Id == id)) return NotFound();
                else throw;
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: Employees/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var employee = await _context.Users.FindAsync(id);
            if (employee != null)
            {
                _context.Users.Remove(employee);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم حذف الموظف";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}