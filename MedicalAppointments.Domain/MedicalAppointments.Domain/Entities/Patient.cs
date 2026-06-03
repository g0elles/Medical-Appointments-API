using System;
using System.Collections.Generic;
using System.Text;

namespace MedicalAppointments.Domain.Entities
{
    public class Patient : User
    {
        public string? Phone { get; set; }
        public List<Appointment> Appointments { get; set; }
    }
}
