using MedicalAppointments.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace MedicalAppointments.Domain.Entities
{
    public class DoctorAvailability : BaseEntity
    {
      public Guid DoctorId { get; set; }
      public DayOfWeek DayOfWeek { get; set; }
      public TimeOnly StartTime { get; set; }
      public TimeOnly EndTime { get; set; }
      public int SlotDuration { get; set; }
    }
}
