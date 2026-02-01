using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NabdAltamayyuz.Models
{
    // أنواع المستخدمين
    public enum UserRole
    {
        SuperAdmin = 1,
        CompanyAdmin = 2, // المشرف الرئيسي
        SubAdmin = 3,     // المشرف الفرعي
        Employee = 4
    }

    // طرق السداد
    public enum PaymentTerm
    {
        [Display(Name = "شهري")] Monthly = 1,
        [Display(Name = "3 شهور")] Quarterly = 3,
        [Display(Name = "6 شهور")] SemiAnnual = 6,
        [Display(Name = "سنوي")] Annual = 12
    }

    // حالة المهمة
    public enum TaskStatus
    {
        [Display(Name = "قيد الانتظار")] Pending,
        [Display(Name = "منجز")] Completed,
        [Display(Name = "غير منجز")] NotCompleted,
        [Display(Name = "مؤجل")] Delayed,
        [Display(Name = "ملغي")] Cancelled
    }

    // جدول الشركات (يشمل الرئيسية والفرعية)
    public class Company
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "اسم الشركة مطلوب")]
        [Display(Name = "اسم الشركة")]
        public string Name { get; set; }

        [Display(Name = "الرقم الموحد")]
        public string UnifiedNumber { get; set; }

        [Display(Name = "رقم السجل التجاري")]
        public string RegistrationNumber { get; set; }

        [Display(Name = "الرقم الضريبي")]
        public string TaxNumber { get; set; }

        [EmailAddress]
        [Display(Name = "البريد الإلكتروني")]
        public string Email { get; set; }

        [Display(Name = "رقم التواصل")]
        public string PhoneNumber { get; set; }

        [Display(Name = "اسم المسؤول")]
        public string ResponsiblePerson { get; set; }

        [Display(Name = "الرقم المختصر للعنوان الوطني")]
        public string NationalAddressShortCode { get; set; }

        // بيانات الاشتراك
        [DataType(DataType.Date)]
        [Display(Name = "تاريخ بداية الاشتراك")]
        public DateTime SubscriptionStartDate { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "تاريخ نهاية الاشتراك")]
        public DateTime SubscriptionEndDate { get; set; }

        [Display(Name = "بيان السداد")]
        public PaymentTerm PaymentTerm { get; set; }

        [Display(Name = "تنبيه قبل الانتهاء (يوم)")]
        public int NotificationDaysBeforeExpiry { get; set; }

        // الحدود والصلاحيات
        [Display(Name = "عدد الحسابات الفرعية المسموحة")]
        public int AllowedSubAccounts { get; set; } = 0;

        [Display(Name = "عدد الموظفين المسموح")]
        public int AllowedEmployees { get; set; } = 10;

        // الحسابات المالية
        [Display(Name = "قيمة الاشتراك لكل موظف (غير شامل الضريبة)")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal PricePerEmployee { get; set; }

        [Display(Name = "نسبة الضريبة %")]
        [Column(TypeName = "decimal(5, 2)")]
        public decimal TaxRate { get; set; } = 15.0m;

        [Display(Name = "إجمالي الاشتراك لكل موظف")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal TotalPricePerEmployee { get; private set; } // يحسب تلقائياً

        public void CalculateTotal()
        {
            TotalPricePerEmployee = PricePerEmployee + (PricePerEmployee * (TaxRate / 100));
        }

        [Display(Name = "المرفقات")]
        public string? AttachmentPath { get; set; }

        [Display(Name = "تعليق الحساب")]
        public bool IsSuspended { get; set; } = false;

        [Display(Name = "تاريخ الإضافة")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // العلاقات
        public int? ParentCompanyId { get; set; } // للشركات الفرعية
        [ForeignKey("ParentCompanyId")]
        public virtual Company? ParentCompany { get; set; }

        public virtual ICollection<Company> SubCompanies { get; set; }
        public virtual ICollection<ApplicationUser> Employees { get; set; }
    }

    // جدول المستخدمين (مشرفين وموظفين)
    public class ApplicationUser
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Display(Name = "الاسم الكامل")]
        public string FullName { get; set; }

        [Display(Name = "رقم الهوية / الإقامة")]
        public string NationalId { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        public string PasswordHash { get; set; }

        public UserRole Role { get; set; }

        [Display(Name = "المسمى الوظيفي")]
        public string JobTitle { get; set; }

        [Display(Name = "رقم الجوال")]
        public string PhoneNumber { get; set; }

        [Display(Name = "الحالة")]
        public string Status { get; set; } = "Active"; // Active, Inactive

        [Display(Name = "تعليق الحساب")]
        public bool IsSuspended { get; set; } = false;

        [Display(Name = "المستندات")]
        public string? AttachmentPath { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public int? CompanyId { get; set; }
        [ForeignKey("CompanyId")]
        public virtual Company Company { get; set; }
    }

    // جدول المهام
    public class WorkTask
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Display(Name = "المهمة")]
        public string Title { get; set; }

        [Display(Name = "الوصف")]
        public string Description { get; set; }

        [Display(Name = "حالة المهمة")]
        public TaskStatus Status { get; set; } = TaskStatus.Pending;

        // تمت الإضافة لحل الخطأ، وتعمل بالتوافق مع Status
        public bool IsCompleted { get; set; } = false;

        [Display(Name = "سبب الحالة")]
        public string? StatusReason { get; set; } // سبب الإنجاز أو التأجيل

        public int AssignedToId { get; set; }
        [ForeignKey("AssignedToId")]
        public virtual ApplicationUser AssignedTo { get; set; }

        public int CreatedById { get; set; } // المشرف
        [ForeignKey("CreatedById")]
        public virtual ApplicationUser CreatedBy { get; set; }

        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime DueDate { get; set; }

        public string? AttachmentPath { get; set; }
    }

    // جدول الحضور
    public class Attendance
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }
        [ForeignKey("EmployeeId")]
        public virtual ApplicationUser Employee { get; set; }

        [DataType(DataType.Date)]
        public DateTime Date { get; set; }

        public string DayName { get; set; } // سبت، أحد...

        public DateTime? TimeIn { get; set; }
        public DateTime? TimeOut { get; set; }

        public bool IsManualEntry { get; set; }
        public string? Notes { get; set; }
    }
}