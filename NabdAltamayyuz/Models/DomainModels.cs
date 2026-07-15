using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NabdAltamayyuz.Models
{
    // أنواع المستخدمين وطرق السداد وحالة المهام (كما هي سابقاً)
    public enum UserRole { SuperAdmin = 1, CompanyAdmin = 2, SubAdmin = 3, Employee = 4 }
    public enum PaymentTerm { [Display(Name = "شهري")] Monthly = 1, [Display(Name = "3 شهور")] Quarterly = 3, [Display(Name = "6 شهور")] SemiAnnual = 6, [Display(Name = "سنوي")] Annual = 12 }
    public enum TaskStatus { [Display(Name = "قيد الانتظار")] Pending, [Display(Name = "منجز")] Completed, [Display(Name = "غير منجز")] NotCompleted, [Display(Name = "مؤجل")] Delayed, [Display(Name = "ملغي")] Cancelled }

    // --- الإضافات الجديدة ---
    public enum LeaveType
    {
        [Display(Name = "سنوي")] Annual,
        [Display(Name = "بدون أجر")] Unpaid,
        [Display(Name = "مرضي")] Sick,
        [Display(Name = "زواج")] Marriage,
        [Display(Name = "مولود")] Maternity,
        [Display(Name = "اختبارات")] Exams,
        [Display(Name = "حج")] Hajj,
        [Display(Name = "وفاة")] Death,
        [Display(Name = "أخرى")] Other
    }

    public enum LeaveStatus { [Display(Name = "معلق")] Pending, [Display(Name = "مقبول")] Approved, [Display(Name = "مرفوض")] Rejected }

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

        [Display(Name = "رقم مكتب العمل للشركة")]
        public string? EstLaborOfficeId { get; set; }

        [Display(Name = "الرقم التسلسلي للشركة")]
        public string? EstSequenceNumber { get; set; }

        [EmailAddress]
        [Display(Name = "البريد الإلكتروني")]
        public string Email { get; set; }

        [Display(Name = "رقم التواصل")]
        public string PhoneNumber { get; set; }

        [Display(Name = "اسم المسؤول")]
        public string ResponsiblePerson { get; set; }

        [Display(Name = "الرقم المختصر للعنوان الوطني")]
        public string NationalAddressShortCode { get; set; }

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

        [Display(Name = "عدد الحسابات الفرعية المسموحة")]
        public int AllowedSubAccounts { get; set; } = 0;

        [Display(Name = "عدد الموظفين المسموح")]
        public int AllowedEmployees { get; set; } = 10;

        [Display(Name = "قيمة الاشتراك لكل موظف (غير شامل الضريبة)")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal PricePerEmployee { get; set; }

        [Display(Name = "نسبة الضريبة %")]
        [Column(TypeName = "decimal(5, 2)")]
        public decimal TaxRate { get; set; } = 15.0m;

        [Display(Name = "إجمالي الاشتراك لكل موظف")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal TotalPricePerEmployee { get; private set; }

        public void CalculateTotal()
        {
            TotalPricePerEmployee = PricePerEmployee + (PricePerEmployee * (TaxRate / 100));
        }

        public string? AttachmentPath { get; set; }
        public bool IsSuspended { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public int? ParentCompanyId { get; set; }
        [ForeignKey("ParentCompanyId")]
        public virtual Company? ParentCompany { get; set; }

        public virtual ICollection<Company> SubCompanies { get; set; }
        public virtual ICollection<ApplicationUser> Employees { get; set; }
        public virtual ICollection<Project> Projects { get; set; }
    }

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

        public string Status { get; set; } = "Active";
        public bool IsSuspended { get; set; } = false;
        public string? AttachmentPath { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // التعديلات الجديدة للموظف
        [DataType(DataType.Date)]
        [Display(Name = "تاريخ التعيين")]
        public DateTime? HireDate { get; set; }

        public int? CompanyId { get; set; }
        [ForeignKey("CompanyId")]
        public virtual Company Company { get; set; }

        public int? ProjectId { get; set; }
        [ForeignKey("ProjectId")]
        public virtual Project? Project { get; set; }

        public int? ProjectJobRoleId { get; set; }
        [ForeignKey("ProjectJobRoleId")]
        public virtual ProjectJobRole? ProjectJobRole { get; set; }
    }

    public class Project
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }
        public string Description { get; set; }

        public int CompanyId { get; set; }
        [ForeignKey("CompanyId")]
        public virtual Company Company { get; set; }

        public virtual ICollection<ProjectJobRole> JobRoles { get; set; }
        public virtual ICollection<ApplicationUser> Employees { get; set; }
    }

    public class ProjectJobRole
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string Name { get; set; }

        public int ProjectId { get; set; }
        [ForeignKey("ProjectId")]
        public virtual Project Project { get; set; }
    }

    public class LeaveRequest
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }
        [ForeignKey("EmployeeId")]
        public virtual ApplicationUser Employee { get; set; }

        public LeaveType Type { get; set; }
        public string? CustomTypeName { get; set; } // يستخدم إذا كان النوع "أخرى"

        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        public int DurationDays { get; set; }
        public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class MonthlyInteraction
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }
        [ForeignKey("EmployeeId")]
        public virtual ApplicationUser Employee { get; set; }

        public DateTime MonthYear { get; set; } // مثلا 01-07-2026

        public double RequiredHours { get; set; } = 176; // المقرر الافتراضي
        public double CompletedHours { get; set; } // المنجز (يُحسب تلقائياً أو يُعدل يدوياً)

        // المهام المسندة والمنجزة لهذا الشهر
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }

        public bool IsManuallyEdited { get; set; } = false;

        public double InteractionPercentage
        {
            get
            {
                if (RequiredHours <= 0) return 0;
                var percentage = (CompletedHours / RequiredHours) * 100;
                return percentage > 100 ? 100 : percentage;
            }
        }

        public double TasksPercentage
        {
            get
            {
                if (TotalTasks <= 0) return 0;
                return ((double)CompletedTasks / TotalTasks) * 100;
            }
        }
    }

    // جداول العمل والمهام (تبقى كما هي)
    public class WorkTask
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string Title { get; set; }
        public string Description { get; set; }
        public TaskStatus Status { get; set; } = TaskStatus.Pending;
        public bool IsCompleted { get; set; } = false;
        public string? StatusReason { get; set; }

        public int AssignedToId { get; set; }
        [ForeignKey("AssignedToId")]
        public virtual ApplicationUser AssignedTo { get; set; }

        public int CreatedById { get; set; }
        [ForeignKey("CreatedById")]
        public virtual ApplicationUser CreatedBy { get; set; }

        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }
        [DataType(DataType.Date)]
        public DateTime DueDate { get; set; }
        public string? AttachmentPath { get; set; }
    }

    public class Attendance
    {
        [Key]
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        [ForeignKey("EmployeeId")]
        public virtual ApplicationUser Employee { get; set; }

        [DataType(DataType.Date)]
        public DateTime Date { get; set; }
        public string DayName { get; set; }
        public DateTime? TimeIn { get; set; }
        public DateTime? TimeOut { get; set; }
        public bool IsManualEntry { get; set; }
        public string? Notes { get; set; }
    }
}