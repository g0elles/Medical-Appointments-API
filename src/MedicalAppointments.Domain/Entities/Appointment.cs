using MedicalAppointments.Domain.Common;
using MedicalAppointments.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace MedicalAppointments.Domain.Entities
{
    public class Appointment : BaseEntity
    {
        public Guid PatientId { get; set; }
        public Guid DoctorId { get; set; }
        public DateTime ScheduleAt { get; set; }
        public int DurationMin { get; set; }
        public AppointmentStatus Status { get; set; }
        public string? Notes { get; set; }
        public string? Idempotency { get; set; }
    }
}
