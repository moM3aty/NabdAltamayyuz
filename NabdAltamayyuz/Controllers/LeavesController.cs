using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Data;
using NabdAltamayyuz.Models;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NabdAltamayyuz.Controllers
{
    [Authorize]
    public class LeavesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public LeavesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // POST: Leaves/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(LeaveRequest model)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            if (model.EmployeeId == 0)
            {
                model.EmployeeId = userId;
            }

            model.CreatedAt = DateTime.Now;

            // حساب المدة
            if (model.EndDate >= model.StartDate)
            {
                model.DurationDays = (model.EndDate - model.StartDate).Days + 1;
            }
            else
            {
                model.DurationDays = 0;
            }

            if (User.IsInRole("SuperAdmin") || User.IsInRole("CompanyAdmin") || User.IsInRole("SubAdmin"))
            {
                model.Status = LeaveStatus.Approved;
            }
            else
            {
                model.Status = LeaveStatus.Pending;
            }

            _context.LeaveRequests.Add(model);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حفظ طلب الإجازة بنجاح.";

            // العودة للصفحة التي جاء منها الطلب
            var referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer))
            {
                return Redirect(referer);
            }

            return RedirectToAction("Index", "Dashboard");
        }

        // POST: Leaves/UpdateStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> UpdateStatus(int id, LeaveStatus status, string returnUrl)
        {
            var leave = await _context.LeaveRequests.Include(l => l.Employee).FirstOrDefaultAsync(l => l.Id == id);
            if (leave == null) return NotFound();

            if (!User.IsInRole("SuperAdmin"))
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(currentUserId);
                if (leave.Employee.CompanyId != currentUser.CompanyId) return Forbid();
            }

            leave.Status = status;
            _context.Update(leave);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم تحديث حالة الإجازة بنجاح";

            if (!string.IsNullOrEmpty(returnUrl)) return Redirect(returnUrl);
            return RedirectToAction("Details", "Employees", new { id = leave.EmployeeId });
        }

        // GET: Leaves/Print/5
        public async Task<IActionResult> Print(int id)
        {
            var leave = await _context.LeaveRequests
                .Include(l => l.Employee).ThenInclude(e => e.Company)
                .Include(l => l.Employee).ThenInclude(e => e.Project) // جلب المشروع
                .FirstOrDefaultAsync(l => l.Id == id);

            if (leave == null) return NotFound();

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            if (!User.IsInRole("SuperAdmin"))
            {
                if (User.IsInRole("Employee") && leave.EmployeeId != userId)
                {
                    return Forbid();
                }
                else if (User.IsInRole("CompanyAdmin") || User.IsInRole("SubAdmin"))
                {
                    var currentUser = await _context.Users.FindAsync(userId);
                    if (leave.Employee.CompanyId != currentUser.CompanyId) return Forbid();
                }
            }

            return View(leave);
        }
    }
}