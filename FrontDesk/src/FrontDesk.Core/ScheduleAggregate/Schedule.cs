using System;
using System.Collections.Generic;
using System.Linq;

using Ardalis.GuardClauses;

using FrontDesk.Core.Events;

using PluralsightDdd.SharedKernel;
using PluralsightDdd.SharedKernel.Interfaces;

namespace FrontDesk.Core.ScheduleAggregate
{
  public class Schedule : BaseEntity<Guid>, IAggregateRoot
  {
    public Schedule(Guid id,
      DateTimeOffsetRange dateRange,
      int clinicId)
    {
      Id = Guard.Against.Default(id, nameof(id));
      DateRange = dateRange;
      ClinicId = Guard.Against.NegativeOrZero(clinicId, nameof(clinicId));
    }

    private Schedule(Guid id, int clinicId) // used by EF
    {
      Id = id;
      ClinicId = clinicId;
    }

    public int ClinicId { get; private set; }
    private readonly List<Appointment> _appointments = new List<Appointment>();
    public IEnumerable<Appointment> Appointments => _appointments.AsReadOnly();

    public DateTimeOffsetRange DateRange { get; private set; }

    public void AddNewAppointment(Appointment appointment)
    {
      Guard.Against.Null(appointment, nameof(appointment));
      Guard.Against.Default(appointment.Id, nameof(appointment.Id));
      Guard.Against.DuplicateAppointment(_appointments, appointment, nameof(appointment));

      _appointments.Add(appointment);

      MarkConflictingAppointments();

      var appointmentScheduledEvent = new AppointmentScheduledEvent(appointment);
      Events.Add(appointmentScheduledEvent);
    }

    public void DeleteAppointment(Appointment appointment)
    {
      Guard.Against.Null(appointment, nameof(appointment));
      var appointmentToDelete = _appointments
                                .Where(a => a.Id == appointment.Id)
                                .FirstOrDefault();

      if (appointmentToDelete != null)
      {
        _appointments.Remove(appointmentToDelete);
      }

      MarkConflictingAppointments();

      AppointmentDeletedEvent appointmentDeletedEvent = new(appointment);
      Events.Add(appointmentDeletedEvent); // TODO-DONE: Add appointment deleted event and show delete message in Blazor client app
    }

    private void MarkConflictingAppointments()
    {
      foreach (var appointment in _appointments)
      {
        // same patient cannot have two appointments at same time
        var potentiallyConflictingAppointments = _appointments
            .Where(a => a.PatientId == appointment.PatientId &&
            a.TimeRange.Overlaps(appointment.TimeRange) &&
            a != appointment)
            .ToList();

        var overlappingAppointments = _appointments
          .Where(a => a.TimeRange.Overlaps(appointment.TimeRange) && a.RoomId == appointment.RoomId && a != appointment).ToList();

        var sameDoctorAppointments = _appointments
          .Where(a => a.TimeRange.Overlaps(appointment.TimeRange) && a.DoctorId == appointment.DoctorId && a!= appointment).ToList();

        potentiallyConflictingAppointments.AddRange(overlappingAppointments);
        potentiallyConflictingAppointments.AddRange(sameDoctorAppointments);

        potentiallyConflictingAppointments.ForEach(a => a.IsPotentiallyConflicting = true);

        appointment.IsPotentiallyConflicting = potentiallyConflictingAppointments.Any();
      }
    }

    /// <summary>
    /// Call any time this schedule's appointments are updated directly
    /// </summary>
    public void AppointmentUpdatedHandler()
    {
      // TODO: Add ScheduleHandler calls to UpdateDoctor, UpdateRoom to complete additional rules described in MarkConflictingAppointments
      MarkConflictingAppointments();
    }
  }
}
