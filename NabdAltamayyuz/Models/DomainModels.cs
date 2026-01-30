using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NabdAltamayyuz.Models
{
    // 1. Enum for User Roles (أنواع المستخدمين)
    public enum UserRole
    {
        SuperAdmin = 1,  // مالك الموقع
        CompanyAdmin = 2, // أدمن الشركة
        Employee = 3     // الموظف
    }

    // 2. Company Entity (الشركات)
    public class Company
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "اسم الشركة مطلوب")]
        [Display(Name = "اسم الشركة")]
        public string Name { get; set; }

        [Display(Name = "السجل التجاري")]
        public string RegistrationNumber { get; set; }

        [Display(Name = "تاريخ الإضافة")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation Property: علاقة واحد لمتعدد مع الموظفين
        public virtual ICollection<ApplicationUser> Employees { get; set; }
    }

    // 3. User Entity (المستخدمين - موظفين ومدراء)
    public class ApplicationUser
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "الاسم الكامل مطلوب")]
        [Display(Name = "الاسم الكامل")]
        public string FullName { get; set; }

        [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
        [EmailAddress(ErrorMessage = "صيغة البريد الإلكتروني غير صحيحة")]
        [Display(Name = "البريد الإلكتروني")]
        public string Email { get; set; }

        [Required]
        public string PasswordHash { get; set; } // يتم تخزين كلمة المرور

        public UserRole Role { get; set; }

        // بيانات وظيفية
        [Display(Name = "المسمى الوظيفي")]
        public string JobTitle { get; set; }

        [Display(Name = "رقم الجوال")]
        public string PhoneNumber { get; set; }

        [Display(Name = "تاريخ التسجيل")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Foreign Key to Company
        public int? CompanyId { get; set; }

        [ForeignKey("CompanyId")]
        public virtual Company Company { get; set; }
    }

    // 4. Attendance Entity (سجل الحضور)
    public class Attendance
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int EmployeeId { get; set; }

        [ForeignKey("EmployeeId")]
        public virtual ApplicationUser Employee { get; set; }

        [DataType(DataType.Date)]
        public DateTime Date { get; set; } // تاريخ اليوم فقط بدون وقت

        [Display(Name = "وقت الدخول")]
        public DateTime? TimeIn { get; set; } // وقت الدخول الفعلي

        [Display(Name = "وقت الخروج")]
        public DateTime? TimeOut { get; set; } // وقت الخروج الفعلي

        public bool IsManualEntry { get; set; } // هل تم إضافته يدوياً بواسطة المدير؟

        [Display(Name = "ملاحظات")]
        public string? Notes { get; set; } // تم التعديل: أصبح يقبل Null
    }

    // 5. Task Entity (المهام)
    public class WorkTask
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "عنوان المهمة مطلوب")]
        [Display(Name = "عنوان المهمة")]
        public string Title { get; set; }

        [Display(Name = "تفاصيل المهمة")]
        public string Description { get; set; }

        // الموظف المسند إليه المهمة
        [Required(ErrorMessage = "يجب اختيار موظف")]
        [Display(Name = "مسندة إلى")]
        public int AssignedToId { get; set; }

        [ForeignKey("AssignedToId")]
        public virtual ApplicationUser AssignedTo { get; set; }

        // المدير الذي أنشأ المهمة
        public int CreatedById { get; set; }

        [ForeignKey("CreatedById")]
        public virtual ApplicationUser CreatedBy { get; set; }

        [Required(ErrorMessage = "تاريخ الاستحقاق مطلوب")]
        [DataType(DataType.Date)]
        [Display(Name = "تاريخ الاستحقاق")]
        public DateTime DueDate { get; set; }

        public bool IsCompleted { get; set; } = false;

        [Display(Name = "مسار المرفق")]
        public string? AttachmentPath { get; set; } // تم التعديل: أصبح يقبل Null
    }
}