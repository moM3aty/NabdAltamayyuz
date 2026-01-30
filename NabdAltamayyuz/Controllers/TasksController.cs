using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

        // GET: Tasks (List for Admin and Employee)
        public async Task<IActionResult> Index()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            if (User.IsInRole(UserRole.Employee.ToString()))
            {
                // Employee sees only their tasks
                var myTasks = await _context.WorkTasks
                    .Include(t => t.CreatedBy)
                    .Where(t => t.AssignedToId == userId)
                    .OrderByDescending(t => t.DueDate)
                    .ToListAsync();
                return View(myTasks);
            }
            else
            {
                // Admin sees all tasks
                var allTasks = await _context.WorkTasks
                    .Include(t => t.AssignedTo)
                    .OrderByDescending(t => t.DueDate)
                    .ToListAsync();
                return View(allTasks);
            }
        }

        // GET: Tasks/Create
        [Authorize(Roles = "CompanyAdmin,SuperAdmin")]
        public IActionResult Create()
        {
            ViewBag.Employees = new SelectList(
                _context.Users.Where(u => u.Role == UserRole.Employee),
                "Id",
                "FullName");

            return View();
        }

        // POST: Tasks/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "CompanyAdmin,SuperAdmin")]
        public async Task<IActionResult> Create(WorkTask model, IFormFile attachment)
        {
            ModelState.Remove("AssignedTo");
            ModelState.Remove("CreatedBy");
            if (ModelState.IsValid)
            {
                if (attachment != null && attachment.Length > 0)
                {
                    model.AttachmentPath = await UploadFile(attachment);
                }

                model.CreatedById = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                model.IsCompleted = false;

                _context.Add(model);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم إرسال المهمة للموظف بنجاح";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.Employees = new SelectList(
                _context.Users.Where(u => u.Role == UserRole.Employee),
                "Id",
                "FullName");

            return View(model);
        }

        // --- NEW: Edit Actions ---

        // GET: Tasks/Edit/5
        [Authorize(Roles = "CompanyAdmin,SuperAdmin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var task = await _context.WorkTasks.FindAsync(id);
            if (task == null) return NotFound();

            ViewBag.Employees = new SelectList(
                _context.Users.Where(u => u.Role == UserRole.Employee),
                "Id",
                "FullName", task.AssignedToId);

            return View(task);
        }

        // POST: Tasks/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "CompanyAdmin,SuperAdmin")]
        public async Task<IActionResult> Edit(int id, WorkTask model, IFormFile attachment)
        {
            if (id != model.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    // 1. Handle New File Upload if exists
                    if (attachment != null && attachment.Length > 0)
                    {
                        // Optional: Delete old file logic could be added here
                        model.AttachmentPath = await UploadFile(attachment);
                    }
                    // Else: model.AttachmentPath comes from the hidden field in the view (Old value)

                    _context.Update(model);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "تم تحديث المهمة بنجاح";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.WorkTasks.Any(e => e.Id == id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }

            ViewBag.Employees = new SelectList(
                _context.Users.Where(u => u.Role == UserRole.Employee),
                "Id",
                "FullName", model.AssignedToId);
            return View(model);
        }

        // POST: Tasks/Complete/5
        [HttpPost]
        public async Task<IActionResult> Complete(int id)
        {
            var task = await _context.WorkTasks.FindAsync(id);
            if (task == null) return NotFound();

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            if (!User.IsInRole("CompanyAdmin") && !User.IsInRole("SuperAdmin") && task.AssignedToId != userId)
            {
                return Forbid();
            }

            task.IsCompleted = true;
            _context.Update(task);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "تم إنجاز المهمة" });
        }

        // Helper Method for File Upload
        private async Task<string> UploadFile(IFormFile file)
        {
            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            string uniqueFileName = Guid.NewGuid().ToString() + "_" + file.FileName;
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }
            return uniqueFileName;
        }
    }
}