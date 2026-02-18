using System;
using System.ComponentModel.DataAnnotations;

namespace NabdAltamayyuz.Models
{
    public class AttendanceViewModel
    {
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        [Display(Name = "الموظف")]
        public string EmployeeName { get; set; }

        [Display(Name = "الوظيفة")]
        public string JobTitle { get; set; }

        [Display(Name = "الشركة")]
        public string CompanyName { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "التاريخ")]
        public DateTime Date { get; set; }

        [Display(Name = "اليوم")]
        public string DayName { get; set; }

        [Display(Name = "وقت الدخول")]
        public DateTime? TimeIn { get; set; }

        [Display(Name = "وقت الخروج")]
        public DateTime? TimeOut { get; set; }

        [Display(Name = "ملاحظات")]
        public string Notes { get; set; }

        public bool IsManualEntry { get; set; }

        // خصائص محسوبة للعرض فقط (Read-Only)

        [Display(Name = "الحالة")]
        public string Status
        {
            get
            {
                if (TimeIn == null) return "غياب";
                if (TimeOut == null) return "جاري العمل";
                return "مكتمل";
            }
        }

        [Display(Name = "ساعات العمل")]
        public double WorkHours
        {
            get
            {
                if (TimeIn.HasValue && TimeOut.HasValue)
                {
                    return (TimeOut.Value - TimeIn.Value).TotalHours;
                }
                return 0;
            }
        }

        // خاصية مساعدة لتحديد لون الحالة (Badges)
        public string StatusBadgeClass
        {
            get
            {
                if (TimeIn == null) return "bg-danger"; // غياب
                if (TimeOut == null) return "bg-warning text-dark"; // جاري
                return "bg-success"; // مكتمل
            }
        }
    }
}