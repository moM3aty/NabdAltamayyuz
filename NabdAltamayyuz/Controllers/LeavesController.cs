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

        // POST: Leaves/Create (للموظف أو المشرف نيابة عن الموظف)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(LeaveRequest model)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            // تحديد من هو الموظف صاحب الإجازة
            if (model.EmployeeId == 0)
            {
                model.EmployeeId = userId;
            }

            model.CreatedAt = DateTime.Now;

            // حساب المدة إذا لم يتم تمريرها بشكل صحيح
            if (model.EndDate >= model.StartDate)
            {
                model.DurationDays = (model.EndDate - model.StartDate).Days + 1; // +1 لجعل الأيام شاملة
            }
            else
            {
                model.DurationDays = 0;
            }

            // إذا كان مقدم الطلب مشرفاً، يتم قبولها تلقائياً، وإلا فهي معلقة
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

            TempData["Success"] = "تم إرسال طلب الإجازة بنجاح.";

            // توجيه العودة حسب الصلاحية (للموظف للوحة الخاصة به، وللمشرف لصفحة تفاصيل الموظف)
            if (User.IsInRole("Employee"))
            {
                return RedirectToAction("Employee", "Dashboard");
            }
            return RedirectToAction("Details", "Employees", new { id = model.EmployeeId });
        }

        // POST: Leaves/UpdateStatus (للمشرفين)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "CompanyAdmin,SuperAdmin,SubAdmin")]
        public async Task<IActionResult> UpdateStatus(int id, LeaveStatus status)
        {
            var leave = await _context.LeaveRequests.Include(l => l.Employee).FirstOrDefaultAsync(l => l.Id == id);
            if (leave == null) return NotFound();

            // التحقق من الصلاحيات (يجب أن يكون المشرف من نفس شركة الموظف أو سوبر أدمن)
            if (!User.IsInRole("SuperAdmin"))
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var currentUser = await _context.Users.FindAsync(currentUserId);
                if (leave.Employee.CompanyId != currentUser.CompanyId) return Forbid();
            }

            leave.Status = status;
            _context.Update(leave);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم تحديث حالة الإجازة إلى: {status}";
            return RedirectToAction("Details", "Employees", new { id = leave.EmployeeId });
        }

        // GET: Leaves/Print/5 (نموذج الطباعة)
        public async Task<IActionResult> Print(int id)
        {
            var leave = await _context.LeaveRequests
                .Include(l => l.Employee)
                .ThenInclude(e => e.Company)
                .Include(l => l.Employee)
                .ThenInclude(e => e.Project)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (leave == null) return NotFound();

            // الصلاحيات: الموظف يمكنه طباعة إجازته فقط، والمشرف للموظفين في شركته
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