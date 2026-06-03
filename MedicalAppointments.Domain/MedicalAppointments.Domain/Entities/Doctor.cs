using System;
using System.Collections.Generic;
using System.Text;

namespace MedicalAppointments.Domain.Entities
{
    internal class Doctor : User
    {
        public string Specialty { get; set; }
        public string? Bio { get; set; }
        public List<DoctorAvailability> Availabilities { get; set; }
        public List<Appointment> Appointments { get; set; }
    }
}
