using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NabdAltamayyuz.Data;
using NabdAltamayyuz.Models;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NabdAltamayyuz.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Account/Login
        public IActionResult Login()
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Dashboard");
            }
            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "الرجاء إدخال البريد الإلكتروني وكلمة المرور";
                return View();
            }

            email = email.Trim();

            // 1. البحث عن المستخدم بالبريد الإلكتروني فقط أولاً
            var user = await _context.Users
                .Include(u => u.Company)
                .FirstOrDefaultAsync(u => u.Email == email);

            // 2. التحقق من وجود المستخدم
            if (user == null)
            {
                ViewBag.Error = $"المستخدم غير موجود. تأكد من صحة البريد الإلكتروني: {email}";
                return View();
            }

            // 3. التحقق من كلمة المرور
            if (user.PasswordHash != password)
            {
                ViewBag.Error = "كلمة المرور غير صحيحة.";
                return View();
            }

            // 4. التحقق من حالة الحساب (معلق أو غير نشط)
            bool isCompanySuspended = user.Company != null && user.Company.IsSuspended;

            if (user.IsSuspended)
            {
                ViewBag.Error = "عذراً، هذا الحساب معلق شخصياً. يرجى مراجعة الإدارة.";
                return View();
            }

            if (isCompanySuspended)
            {
                ViewBag.Error = "عذراً، حساب الشركة معلق. لا يمكن تسجيل الدخول.";
                return View();
            }

            // التعديل هنا: السماح بالدخول إذا كانت الحالة فارغة (null/empty) أو Active
            // هذا يحل مشكلة الحسابات القديمة التي ليس لها حالة مسجلة
            if (!string.IsNullOrEmpty(user.Status) && user.Status != "Active")
            {
                ViewBag.Error = $"عذراً، حالة الحساب هي {user.Status} وليست Active.";
                return View();
            }

            // 5. تسجيل الدخول بنجاح
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.FullName ?? "User"),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = System.DateTime.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            return RedirectToAction("Index", "Dashboard");
        }

        // GET: /Account/Logout
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        // GET: /Account/AccessDenied
        public IActionResult AccessDenied()
        {
            return View();
        }

        // GET: /Account/Profile
        public async Task<IActionResult> Profile()
        {
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Login");

            var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdValue)) return RedirectToAction("Logout");

            var userId = int.Parse(userIdValue);
            var user = await _context.Users
                .Include(u => u.Company)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null) return RedirectToAction("Logout");

            return View(user);
        }
    }
}