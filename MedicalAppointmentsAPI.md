# Medical Appointments API

**Software Requirements & Architecture Specification**

> **Version:** 1.0
> **Author:** Gabri Elles — Senior .NET Engineer
> **Stack:** .NET 10 · EF Core 10 · SQL Server · C# 14 · JWT · RabbitMQ · Redis · xUnit · Swagger / OpenAPI 3.0 · GitHub Actions · Azure App Service
> **Patterns:** Clean Architecture · Repository + Unit of Work · CQRS-lite · Idempotency Keys · Rate Limiting · RFC 7807

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [System Overview](#2-system-overview)
3. [Architecture](#3-architecture)
4. [Domain Model](#4-domain-model)
5. [Functional Requirements](#5-functional-requirements)
6. [Non-Functional Requirements](#6-non-functional-requirements)
7. [API Contract](#7-api-contract)
8. [Data Flows](#8-data-flows)
9. [Infrastructure & Deployment](#9-infrastructure--deployment)
10. [Constraints & Assumptions](#10-constraints--assumptions)
11. [Out of Scope](#11-out-of-scope)
12. [Development Checklist](#12-development-checklist)

---

## 1. Introduction

### 1.1 Purpose

This document defines the functional and non-functional requirements **and** the technical architecture for the Medical Appointments API, a portfolio project showcasing production-grade .NET 10 backend development. It is the single authoritative reference for the project — both a specification to build against and an architecture map to onboard from.

### 1.2 Scope

The system provides a REST API that enables:

- Patient and doctor registration, authentication, and profile management.
- Doctor schedule and availability management.
- Appointment booking, consultation, and cancellation by patients.
- Asynchronous email notifications via RabbitMQ.
- Caching of read-heavy endpoints via Redis.
- Full deployment to Azure with CI/CD via GitHub Actions.

An Angular SPA frontend is planned as a subsequent phase and is outside the scope of this version.

### 1.3 Definitions and Acronyms

| Term | Definition |
|---|---|
| API | Application Programming Interface |
| JWT | JSON Web Token — used for stateless authentication |
| RabbitMQ | Message broker for async communication between services |
| Redis | In-memory data store used for caching and idempotency keys |
| EF Core | Entity Framework Core — .NET ORM for SQL Server |
| Clean Architecture | Layered architecture pattern enforcing dependency inversion |
| UoW | Unit of Work — transactional pattern wrapping repositories |
| Idempotency Key | Client-supplied UUID preventing duplicate operations on retry |
| CQRS-lite | Separation of read (Query) and write (Command) handlers without full event sourcing |
| TPH | Table Per Hierarchy — EF Core inheritance mapping strategy |
| RFC 7807 | Problem Details — standard structured format for HTTP API errors |

### 1.4 References

- Clean Architecture — Robert C. Martin (2017)
- Microsoft .NET 10 Documentation — https://learn.microsoft.com/dotnet
- RabbitMQ Documentation — https://www.rabbitmq.com/docs
- Redis Documentation — https://redis.io/docs
- RFC 7807, Problem Details for HTTP APIs — https://datatracker.ietf.org/doc/html/rfc7807
- OWASP REST Security Cheat Sheet — https://cheatsheetseries.owasp.org

---

## 2. System Overview

### 2.1 System Context

The Medical Appointments API sits at the center of a distributed system. It communicates synchronously with clients (patients and doctors via HTTP REST) and asynchronously with a notification consumer via RabbitMQ. A Redis cache layer reduces database load on frequently queried endpoints and stores idempotency keys.

### 2.2 User Roles

| Role | Description |
|---|---|
| Patient | Registers an account, searches for doctors, books and cancels appointments. |
| Doctor | Registers an account, manages weekly availability, views scheduled appointments. |
| System | Background service consuming RabbitMQ events and sending email notifications. |

### 2.3 Technology Summary

| Concern | Choice |
|---|---|
| Runtime | .NET 10 LTS (supported until November 2028) |
| Language | C# 14 |
| Architecture | Clean Architecture (Domain / Application / Infrastructure / API) |
| ORM | Entity Framework Core 10 + SQL Server |
| Authentication | JWT Bearer tokens + Refresh Token rotation |
| Async messaging | RabbitMQ (appointment events → email notifications) |
| Caching | Redis (available slots, idempotency keys) |
| Idempotency | `Idempotency-Key` header + Redis key store |
| Rate limiting | Token bucket middleware on public endpoints |
| Validation | FluentValidation on all command DTOs |
| Error format | RFC 7807 Problem Details |
| Testing | xUnit + WebApplicationFactory + NetArchTest |
| CI/CD | GitHub Actions → Azure App Service |
| API Documentation | Swagger / OpenAPI 3.0 |

---

## 3. Architecture

### 3.1 Clean Architecture Layers

The solution follows the Clean Architecture dependency rule: dependencies point **inward only**. The Domain layer has no dependencies; each outer layer depends only on the layers inside it. This is enforced automatically by `NetArchTest` rules in the test suite.

```
┌──────────────────────────────────────────────────────────────┐
│                          API Layer                           │
│  AuthController  AppointmentsController  DoctorsController   │
│  PatientsController                                          │
│       │                                                      │
│  IdempotencyMiddleware  RateLimitingMiddleware                │
│  ExceptionHandlingMiddleware (RFC 7807 ProblemDetails)       │
└──────────────────────────┬───────────────────────────────────┘
                           │  Commands / Queries
┌──────────────────────────▼───────────────────────────────────┐
│                      Application Layer                       │
│  RegisterHandler     LoginHandler      RefreshTokenHandler   │
│  CreateAppointmentHandler              CancelAppointmentHandler│
│  GetAvailableSlotsHandler (→ ICacheService)                  │
│  CreateAvailabilityHandler             GetDoctorsHandler      │
│  FluentValidation on all commands                            │
└───────┬──────────────────────────────────────┬───────────────┘
        │ IRepository / IUnitOfWork             │ ICacheService
        │ ITokenService / IMessagePublisher     │
┌───────▼──────────────────┐   ┌───────────────▼───────────────┐
│       Domain Layer        │   │      Infrastructure Layer     │
│                           │   │                               │
│  BaseEntity               │   │  AppDbContext (EF Core 10)    │
│  User                     │   │  SQL Server                   │
│  Doctor                   │   │                               │
│  Patient                  │   │  RedisCacheService            │
│  DoctorAvailability        │   │  RabbitMqPublisher           │
│  Appointment               │   │  AppointmentCreatedConsumer   │
│                           │   │  JwtTokenService              │
│  AppointmentStatus (enum) │   │                               │
│  UserRole (enum)          │   └───────────────────────────────┘
│  SlotUnavailableException  │
└───────────────────────────┘
                    ┌──────────────────────────┐
                    │     External Services    │
                    │  RabbitMQ  (events)      │
                    │  Redis     (cache + idem) │
                    │  SQL Server (data)        │
                    │  Azure App Service        │
                    └──────────────────────────┘
```

### 3.2 Layer Responsibilities

| Layer | Depends on | Responsibility |
|---|---|---|
| **Domain** | nothing | Entities, enums, domain exceptions, repository interfaces. Pure business rules, no framework code. |
| **Application** | Domain | Use cases (command/query handlers), DTOs, validators, service interfaces. Orchestrates the domain. |
| **Infrastructure** | Application, Domain | EF Core, Redis, RabbitMQ, JWT — concrete implementations of interfaces defined inward. |
| **API** | Infrastructure, Application, Domain | Controllers, middleware, dependency injection wiring, the HTTP entry point. |

### 3.3 Solution Structure

```
MedicalAppointments/
│
├── src/
│   ├── MedicalAppointments.Domain/              # Enterprise business rules — no dependencies
│   │   ├── Common/
│   │   │   └── BaseEntity.cs                    # Id, CreatedAt, UpdatedAt — shared base
│   │   ├── Entities/
│   │   │   ├── User.cs                          # extends BaseEntity
│   │   │   ├── Doctor.cs                        # extends User
│   │   │   ├── Patient.cs                       # extends User
│   │   │   ├── DoctorAvailability.cs            # extends BaseEntity
│   │   │   └── Appointment.cs                   # extends BaseEntity
│   │   ├── Enums/
│   │   │   ├── AppointmentStatus.cs             # Scheduled, Confirmed, Completed, Cancelled
│   │   │   ├── DayOfWeek.cs
│   │   │   └── UserRole.cs                      # Patient, Doctor
│   │   ├── Interfaces/
│   │   │   ├── IAppointmentRepository.cs
│   │   │   ├── IDoctorRepository.cs
│   │   │   ├── IPatientRepository.cs
│   │   │   └── IUnitOfWork.cs
│   │   └── Exceptions/
│   │       ├── DomainException.cs
│   │       └── SlotUnavailableException.cs
│   │
│   ├── MedicalAppointments.Application/         # Use cases — depends only on Domain
│   │   ├── Features/
│   │   │   ├── Auth/
│   │   │   │   └── Commands/
│   │   │   │       ├── Register/
│   │   │   │       │   ├── RegisterCommand.cs
│   │   │   │       │   └── RegisterHandler.cs
│   │   │   │       ├── Login/
│   │   │   │       │   ├── LoginCommand.cs
│   │   │   │       │   └── LoginHandler.cs
│   │   │   │       └── RefreshToken/
│   │   │   │           ├── RefreshTokenCommand.cs
│   │   │   │           └── RefreshTokenHandler.cs
│   │   │   ├── Appointments/
│   │   │   │   ├── Commands/
│   │   │   │   │   ├── CreateAppointment/
│   │   │   │   │   │   ├── CreateAppointmentCommand.cs
│   │   │   │   │   │   └── CreateAppointmentHandler.cs
│   │   │   │   │   └── CancelAppointment/
│   │   │   │   │       ├── CancelAppointmentCommand.cs
│   │   │   │   │       └── CancelAppointmentHandler.cs
│   │   │   │   └── Queries/
│   │   │   │       ├── GetMyAppointments/
│   │   │   │       │   ├── GetMyAppointmentsQuery.cs
│   │   │   │       │   └── GetMyAppointmentsHandler.cs
│   │   │   │       └── GetAppointmentById/
│   │   │   │           ├── GetAppointmentByIdQuery.cs
│   │   │   │           └── GetAppointmentByIdHandler.cs
│   │   │   └── Doctors/
│   │   │       ├── Commands/
│   │   │       │   ├── CreateAvailability/
│   │   │       │   │   ├── CreateAvailabilityCommand.cs
│   │   │       │   │   └── CreateAvailabilityHandler.cs
│   │   │       │   └── DeleteAvailability/
│   │   │       │       ├── DeleteAvailabilityCommand.cs
│   │   │       │       └── DeleteAvailabilityHandler.cs
│   │   │       └── Queries/
│   │   │           ├── GetAvailableSlots/
│   │   │           │   ├── GetAvailableSlotsQuery.cs
│   │   │           │   └── GetAvailableSlotsHandler.cs  # cached via ICacheService
│   │   │           └── GetDoctors/
│   │   │               ├── GetDoctorsQuery.cs
│   │   │               └── GetDoctorsHandler.cs
│   │   ├── DTOs/
│   │   │   ├── AppointmentDto.cs
│   │   │   ├── DoctorDto.cs
│   │   │   ├── DoctorAvailabilityDto.cs
│   │   │   ├── PatientDto.cs
│   │   │   └── Auth/
│   │   │       ├── LoginResponseDto.cs          # access token + refresh token
│   │   │       └── RegisterRequestDto.cs
│   │   ├── Interfaces/
│   │   │   ├── ICacheService.cs
│   │   │   ├── ITokenService.cs
│   │   │   └── IMessagePublisher.cs
│   │   └── Validators/                          # FluentValidation
│   │       ├── RegisterValidator.cs
│   │       ├── CreateAppointmentValidator.cs
│   │       └── CreateAvailabilityValidator.cs
│   │
│   ├── MedicalAppointments.Infrastructure/      # Frameworks & drivers — depends on Application
│   │   ├── Persistence/
│   │   │   ├── AppDbContext.cs
│   │   │   ├── Configurations/                  # EF Core IEntityTypeConfiguration<T>
│   │   │   │   ├── UserConfiguration.cs
│   │   │   │   ├── DoctorConfiguration.cs
│   │   │   │   ├── PatientConfiguration.cs
│   │   │   │   ├── DoctorAvailabilityConfiguration.cs
│   │   │   │   └── AppointmentConfiguration.cs
│   │   │   ├── Repositories/
│   │   │   │   ├── AppointmentRepository.cs
│   │   │   │   ├── DoctorRepository.cs
│   │   │   │   └── PatientRepository.cs
│   │   │   ├── UnitOfWork.cs
│   │   │   └── Migrations/
│   │   ├── Messaging/
│   │   │   ├── RabbitMqPublisher.cs             # implements IMessagePublisher
│   │   │   └── Consumers/
│   │   │       └── AppointmentCreatedConsumer.cs # IHostedService, sends email
│   │   ├── Caching/
│   │   │   └── RedisCacheService.cs             # implements ICacheService
│   │   └── Identity/
│   │       └── JwtTokenService.cs               # implements ITokenService
│   │
│   └── MedicalAppointments.API/                 # Entry point — depends on Infrastructure
│       ├── Controllers/
│       │   ├── AuthController.cs                # register, login, refresh, logout
│       │   ├── AppointmentsController.cs        # book, list, get, cancel
│       │   ├── DoctorsController.cs             # profile, availability, slots, appointments
│       │   └── PatientsController.cs            # profile
│       ├── Middleware/
│       │   ├── ExceptionHandlingMiddleware.cs   # RFC 7807 ProblemDetails
│       │   ├── IdempotencyMiddleware.cs         # Idempotency-Key header + Redis
│       │   └── RateLimitingMiddleware.cs        # token bucket, 30 req/min per IP
│       ├── Extensions/
│       │   └── ServiceCollectionExtensions.cs
│       └── Program.cs
│
├── tests/
│   ├── MedicalAppointments.UnitTests/
│   │   ├── Features/
│   │   │   ├── CreateAppointmentHandlerTests.cs
│   │   │   ├── CancelAppointmentHandlerTests.cs
│   │   │   └── GetAvailableSlotsHandlerTests.cs
│   │   └── Domain/
│   │       ├── AppointmentTests.cs
│   │       └── DoctorAvailabilityTests.cs
│   ├── MedicalAppointments.IntegrationTests/
│   │   ├── AppointmentsControllerTests.cs       # WebApplicationFactory
│   │   └── AuthControllerTests.cs
│   └── MedicalAppointments.ArchTests/
│       └── LayerDependencyTests.cs              # NetArchTest rules
│
├── docker-compose.yml                           # SQL Server + Redis + RabbitMQ (local dev)
└── .github/
    └── workflows/
        ├── ci.yml                               # PR → build + test
        ├── cd-staging.yml                       # merge to main → deploy staging
        └── cd-production.yml                    # manual gate → slot swap
```

### 3.4 Key Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Runtime | .NET 10 LTS | Current LTS, supported until Nov 2028; access to C# 14 features. |
| Architecture | Clean Architecture | Domain and Application are fully testable without infrastructure; dependency rule enforced via NetArchTest. |
| **Shared base entity** | **`BaseEntity` abstract class** | **All entities share `Id`, `CreatedAt`, and `UpdatedAt`. Centralizing them in `BaseEntity` avoids repetition and lets the `DbContext` set audit timestamps in one place via `SaveChanges` override.** |
| User inheritance | TPH (Table Per Hierarchy) | `User` / `Doctor` / `Patient` share one table with a discriminator column; simpler queries than separate tables. |
| ORM | EF Core 10 + SQL Server | Strong ecosystem, migrations, LINQ; one `IEntityTypeConfiguration` per entity. |
| Auth | JWT (access 15 min) + Refresh Token (7 days) | Stateless; refresh token persisted on the `User` entity, revoked on logout. |
| Async messaging | RabbitMQ | Decouples appointment creation from email sending; consumer retries up to 3×. |
| Caching | Redis | Available-slots endpoint is read-heavy; 5-minute TTL, invalidated on write. |
| Idempotency | `Idempotency-Key` header + Redis | Prevents duplicate appointments on client retry; key stored with a 24-hour TTL. |
| Rate limiting | Token bucket middleware (30 req/min/IP) | Protects public endpoints; returns 429 with `Retry-After`. |
| Error format | RFC 7807 ProblemDetails | Structured, machine-readable errors on all 4xx/5xx responses. |
| Testing | xUnit + WebApplicationFactory + NetArchTest | Unit (handlers), integration (controllers), architecture (layer rules). |

---

## 4. Domain Model

All entities inherit from `BaseEntity`, which provides the identity and audit fields. Soft delete is implemented via an `IsDeleted` flag where applicable.

### 4.1 BaseEntity (abstract)

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key. |
| CreatedAt | DateTime | UTC creation timestamp. Set automatically on insert. |
| UpdatedAt | DateTime | UTC last-modified timestamp. Set automatically on update. |

> `BaseEntity` is an abstract class in `Domain/Common`. The `AppDbContext` overrides `SaveChangesAsync` to populate `CreatedAt` / `UpdatedAt` for any tracked entity inheriting from it — audit logic lives in exactly one place.

### 4.2 User (extends BaseEntity)

| Field | Type | Notes |
|---|---|---|
| Email | string | Unique. Used for login. Max 256 chars. |
| PasswordHash | string | BCrypt hash (cost ≥ 12). Never stored in plaintext. |
| FullName | string | Max 200 chars. |
| Role | enum | `Patient` \| `Doctor`. |
| RefreshToken | string? | Current refresh token. Null if logged out. |
| RefreshTokenExpiresAt | DateTime? | Expiry of the current refresh token. |

### 4.3 Doctor (extends User)

| Field | Type | Notes |
|---|---|---|
| Specialty | string | Medical specialty. Max 100 chars. |
| Bio | string? | Optional description. Max 1000 chars. |
| Availabilities | List\<DoctorAvailability\> | Navigation property. |
| Appointments | List\<Appointment\> | Navigation property. |

### 4.4 Patient (extends User)

| Field | Type | Notes |
|---|---|---|
| Phone | string? | Optional. Max 20 chars. |
| Appointments | List\<Appointment\> | Navigation property. |

### 4.5 DoctorAvailability (extends BaseEntity)

| Field | Type | Notes |
|---|---|---|
| DoctorId | Guid | Foreign key → Doctor. |
| DayOfWeek | enum | Monday through Sunday. |
| StartTime | TimeOnly | Start of available block. |
| EndTime | TimeOnly | End of available block. |
| SlotDuration | int | Duration of each slot in minutes (e.g., 30). |

### 4.6 Appointment (extends BaseEntity)

| Field | Type | Notes |
|---|---|---|
| PatientId | Guid | Foreign key → Patient. |
| DoctorId | Guid | Foreign key → Doctor. |
| ScheduledAt | DateTime | UTC datetime of the appointment start. |
| DurationMin | int | Duration in minutes (matches slot duration). |
| Status | enum | `Scheduled` \| `Confirmed` \| `Completed` \| `Cancelled`. |
| Notes | string? | Optional notes. Max 2000 chars. |
| IdempotencyKey | string? | Stored to enforce idempotent creation. |

### 4.7 Status Transitions

```
Scheduled ──► Confirmed ──► Completed
    │              │
    └──────────────┴──────► Cancelled
```

Invalid transitions (e.g., `Completed` → `Scheduled`) are rejected by domain logic and return HTTP 422.

---

## 5. Functional Requirements

Priority: **M** = Must Have, **S** = Should Have, **C** = Could Have.

### 5.1 Authentication & Authorization

| ID | Requirement | Acceptance Criteria | Priority |
|---|---|---|---|
| FR-001 | Patients and doctors can register with email, password, full name, and role. | `POST /auth/register` returns 201 with user ID. Duplicate email returns 409. | M |
| FR-002 | Registered users can log in with email and password. | `POST /auth/login` returns JWT access token (15 min) and refresh token (7 days). | M |
| FR-003 | Clients can refresh the access token using a valid refresh token. | `POST /auth/refresh` returns new access token. Expired or reused refresh token returns 401. | M |
| FR-004 | Clients can log out, invalidating the refresh token. | `POST /auth/logout` marks refresh token revoked. Subsequent refresh returns 401. | M |
| FR-005 | All non-auth endpoints require a valid JWT Bearer token. | Requests without/invalid token return 401. Expired token returns 401 with specific error code. | M |
| FR-006 | Doctors cannot book appointments. Patients cannot manage availability. | Role-restricted endpoints return 403 when accessed by the wrong role. | M |

### 5.2 Doctor Availability Management

| ID | Requirement | Acceptance Criteria | Priority |
|---|---|---|---|
| FR-010 | Doctors can define weekly availability (day, start, end, slot duration). | `POST /doctors/availability` stores schedule. `GET /doctors/{id}/availability` returns slots. | M |
| FR-011 | Doctors can update or delete an availability slot. | `PUT` and `DELETE /doctors/availability/{id}` update or remove the slot. | M |
| FR-012 | The system calculates open slots from availability minus existing appointments. | `GET /doctors/{id}/slots?date=YYYY-MM-DD` returns available slots; booked slots excluded. | M |
| FR-013 | Available-slots responses are cached in Redis for 5 minutes. | Repeated calls within 5 min are served from cache; cache invalidated on new appointment or availability change. | S |

### 5.3 Appointment Management

| ID | Requirement | Acceptance Criteria | Priority |
|---|---|---|---|
| FR-020 | Patients can book an appointment at an available slot. | `POST /appointments` returns 201. Double-booking the same slot returns 409. | M |
| FR-021 | Appointment creation is idempotent via `Idempotency-Key` header. | Repeated POST with same key returns the original 201 without creating a duplicate. | M |
| FR-022 | Patients can view their own appointments. | `GET /appointments/my` returns paginated list for the authenticated patient. | M |
| FR-023 | Doctors can view their scheduled appointments. | `GET /doctors/my/appointments` returns paginated list with patient name, date, time. | M |
| FR-024 | Patients can cancel an appointment up to 2 hours before start. | `DELETE /appointments/{id}` returns 204. Cancellation within 2 hours returns 422. | M |
| FR-025 | An `AppointmentCreated` event is published to RabbitMQ after booking. | Queue receives payload: appointment ID, patient email, doctor name, date, time. | M |
| FR-026 | A consumer processes events and sends confirmation emails. | Email delivered to patient address; consumer retries up to 3× on failure. | S |
| FR-027 | Appointment status transitions are enforced. | Status follows Scheduled → Confirmed → Completed / Cancelled. Invalid transitions return 422. | M |

### 5.4 User Profiles

| ID | Requirement | Acceptance Criteria | Priority |
|---|---|---|---|
| FR-030 | Patients can view and update their profile (name, phone). | `GET` and `PUT /patients/me` return and update profile. Email cannot be changed. | M |
| FR-031 | Doctors can view and update their profile (name, specialty, bio). | `GET` and `PUT /doctors/me` return and update profile. | M |
| FR-032 | Patients can search doctors by name or specialty. | `GET /doctors?search=cardio` returns paginated matches. | S |

---

## 6. Non-Functional Requirements

### 6.1 Performance

| ID | Requirement | Target |
|---|---|---|
| NFR-001 | Cached endpoints respond quickly at p95. | p95 < 100 ms under 50 concurrent users. |
| NFR-002 | Non-cached read endpoints respond within target at p95. | p95 < 300 ms under 50 concurrent users. |
| NFR-003 | Write endpoints respond within target at p95. | p95 < 500 ms under 50 concurrent users. |
| NFR-004 | RabbitMQ consumer processes events promptly. | 99% of events processed within 5 seconds. |

### 6.2 Security

| ID | Requirement | Measure |
|---|---|---|
| NFR-010 | Passwords hashed with BCrypt, cost factor ≥ 12. | No plaintext passwords stored. |
| NFR-011 | JWT signed with RS256 or HS256 (256-bit secret). | Algorithm declared in token header; secret in Azure Key Vault. |
| NFR-012 | Public endpoints enforce rate limiting. | Max 30 requests/min per IP; excess returns 429 with `Retry-After`. |
| NFR-013 | All API communication uses HTTPS. | HTTP redirects to HTTPS; HSTS header included. |
| NFR-014 | Patients access only their own appointments. | Accessing another patient's resource returns 403. |
| NFR-015 | Input validation rejects malformed requests before business logic. | FluentValidation returns 400 with structured errors. |

### 6.3 Reliability & Availability

| ID | Requirement | Target |
|---|---|---|
| NFR-020 | API available 99.5% of the time in staging. | Azure health checks + uptime monitoring. |
| NFR-021 | Failed message processing retries with backoff. | Up to 3 retries; then dead-letter queue. |
| NFR-022 | If Redis is unavailable, the API degrades gracefully. | Cache operations wrapped so failures fall back to the database. |
| NFR-023 | Database transactions enforce ACID properties. | Unit of Work wraps all writes in a single transaction. |

### 6.4 Maintainability

| ID | Requirement |
|---|---|
| NFR-030 | Code follows Clean Architecture dependency rules, enforced via NetArchTest. |
| NFR-031 | Unit test coverage ≥ 80% on the Application layer. |
| NFR-032 | All public endpoints documented via Swagger / OpenAPI 3.0. |
| NFR-033 | The GitHub Actions pipeline fails the build if any test fails. |
| NFR-034 | All configuration and secrets externalized — none in source code. |

### 6.5 Scalability

| ID | Requirement |
|---|---|
| NFR-040 | The API is stateless; all state lives in SQL Server or Redis. |
| NFR-041 | The RabbitMQ consumer is independently scalable (separate worker). |
| NFR-042 | Schema changes managed via EF Core migrations — no manual scripts. |

---

## 7. API Contract

All endpoints are prefixed with `/api/v1`. Errors follow RFC 7807 (Problem Details).

### 7.1 Authentication

| Endpoint | Method | Auth | Description |
|---|---|---|---|
| `/auth/register` | POST | None | Register a new patient or doctor account. |
| `/auth/login` | POST | None | Authenticate and receive JWT + refresh token. |
| `/auth/refresh` | POST | None | Exchange refresh token for a new access token. |
| `/auth/logout` | POST | Bearer | Revoke the current refresh token. |

### 7.2 Doctors

| Endpoint | Method | Auth | Description |
|---|---|---|---|
| `/doctors` | GET | Bearer | Search doctors by name or specialty (paginated). |
| `/doctors/{id}` | GET | Bearer | Get a doctor's public profile. |
| `/doctors/me` | GET | Doctor | Get the authenticated doctor's own profile. |
| `/doctors/me` | PUT | Doctor | Update the authenticated doctor's profile. |
| `/doctors/{id}/slots` | GET | Bearer | Get available appointment slots for a date. |
| `/doctors/availability` | POST | Doctor | Create a weekly availability block. |
| `/doctors/availability/{id}` | PUT | Doctor | Update an availability block. |
| `/doctors/availability/{id}` | DELETE | Doctor | Delete an availability block. |
| `/doctors/my/appointments` | GET | Doctor | List the doctor's upcoming appointments. |

### 7.3 Patients

| Endpoint | Method | Auth | Description |
|---|---|---|---|
| `/patients/me` | GET | Patient | Get the authenticated patient's profile. |
| `/patients/me` | PUT | Patient | Update the authenticated patient's profile. |

### 7.4 Appointments

| Endpoint | Method | Auth | Description |
|---|---|---|---|
| `/appointments` | POST | Patient | Book a new appointment. Requires `Idempotency-Key` header. |
| `/appointments/my` | GET | Patient | List the patient's appointments (paginated). |
| `/appointments/{id}` | GET | Bearer | Get appointment details (owner patient or assigned doctor). |
| `/appointments/{id}` | DELETE | Patient | Cancel an appointment (≥ 2 hours before start). |

### 7.5 Standard Response Codes

| Code | Status | When Used |
|---|---|---|
| 200 | OK | Successful GET or PUT. |
| 201 | Created | Successful POST that created a resource. |
| 204 | No Content | Successful DELETE. |
| 400 | Bad Request | Validation failure (FluentValidation errors in body). |
| 401 | Unauthorized | Missing, invalid, or expired JWT. |
| 403 | Forbidden | Authenticated but insufficient role or ownership. |
| 409 | Conflict | Duplicate resource (email, appointment slot). |
| 422 | Unprocessable Entity | Business rule violation (e.g., cancel < 2h before slot). |
| 429 | Too Many Requests | Rate limit exceeded. `Retry-After` header included. |
| 500 | Internal Server Error | Unhandled exception. Error ID returned for tracing. |

---

## 8. Data Flows

### 8.1 Create Appointment

```
Client
  │
  ├─► POST /api/v1/appointments
  │       Authorization: Bearer <access_token>
  │       Idempotency-Key: <uuid>
  │
  ▼
RateLimitingMiddleware  →  check IP bucket
  │
  ▼
IdempotencyMiddleware   →  check Redis for key
  │  hit  → return cached 201 response immediately
  │  miss → continue
  ▼
AppointmentsController.Create()
  │
  ▼
CreateAppointmentHandler
  │  1. FluentValidation → reject malformed input (400)
  │  2. IDoctorRepository.GetAvailableSlots() → check slot is free (409 if taken)
  │  3. new Appointment { Status = Scheduled, IdempotencyKey = key }
  │  4. IUnitOfWork.SaveChangesAsync() → SQL Server transaction
  │  5. IMessagePublisher.Publish(AppointmentCreatedEvent) → RabbitMQ
  │  6. ICacheService.SetAsync(idempotencyKey, response, 24h) → Redis
  │  7. ICacheService.RemoveAsync(availableSlotsKey) → invalidate slot cache
  │
  ▼
201 Created  { appointmentId, scheduledAt, doctorName, status }
  │
  └─► AppointmentCreatedConsumer (IHostedService, async)
        └─► send confirmation email to patient
```

### 8.2 Login & Token Refresh

```
POST /auth/login  { email, password }
  │
  ▼
LoginHandler
  │  1. Look up user by email
  │  2. Verify password (BCrypt)
  │  3. Issue access token (15 min) + refresh token (7 days)
  │  4. Persist refresh token on User entity
  │
  ▼
200 OK  { accessToken, refreshToken }

        ── later, when access token expires ──

POST /auth/refresh  { refreshToken }
  │
  ▼
RefreshTokenHandler
  │  1. Validate refresh token against stored value + expiry
  │  2. Rotate: issue new access + new refresh token
  │  3. Reused/expired token → 401
  │
  ▼
200 OK  { accessToken, refreshToken }
```

### 8.3 Get Available Slots (cached)

```
GET /doctors/{id}/slots?date=YYYY-MM-DD
  │
  ▼
GetAvailableSlotsHandler
  │  1. Build cache key: slots:{doctorId}:{date}
  │  2. ICacheService.GetAsync(key)
  │       hit  → return cached slots
  │       miss → continue
  │  3. Load DoctorAvailability for the day
  │  4. Subtract existing appointments → open slots
  │  5. ICacheService.SetAsync(key, slots, 5 min)
  │
  ▼
200 OK  [ { startTime, endTime }, ... ]
```

---

## 9. Infrastructure & Deployment

### 9.1 Environments

| Environment | Trigger | Notes |
|---|---|---|
| Local | Manual (`dotnet run`) | SQL Server, Redis, RabbitMQ in Docker via `docker-compose`. |
| Staging | Merge to `main` | Azure App Service. Automated deploy via GitHub Actions. |
| Production | Manual approval gate | Azure App Service. Slot swap from staging. Key Vault secrets. |

### 9.2 External Dependencies

| Service | Version / Tier | Purpose |
|---|---|---|
| SQL Server | 2022 / Azure SQL | Primary relational data store. |
| Redis | 7.x / Azure Cache | Response caching and idempotency key store. |
| RabbitMQ | 3.x / CloudAMQP | Async event messaging to the notification consumer. |
| Azure App Service | B1+ plan | Hosting for the API and background worker. |
| Azure Key Vault | Standard tier | Secure storage for connection strings and JWT secrets. |
| GitHub Actions | — | CI/CD pipeline automation. |

### 9.3 CI/CD Pipeline

The pipeline lives in `.github/workflows/` and has three stages:

1. **Build & Test** (`ci.yml`) — triggered on every pull request. Runs `dotnet build` and `dotnet test`; fails fast on test failure.
2. **Deploy to Staging** (`cd-staging.yml`) — triggered on merge to `main`. Builds a Docker image, pushes to Azure Container Registry, deploys to the staging slot, runs a smoke test (`GET /health`).
3. **Deploy to Production** (`cd-production.yml`) — triggered manually after staging approval. Pulls the same immutable image (SHA tag) and performs a slot swap for near-zero-downtime deployment.

### 9.4 Local Development Setup

A `docker-compose.yml` at the repository root starts all dependencies:

- SQL Server 2022 on port 1433
- Redis on port 6379
- RabbitMQ on port 5672 (management UI on 15672)

The API runs with `dotnet run`. Connection strings are managed via `dotnet user-secrets` for local development.

---

## 10. Constraints & Assumptions

### 10.1 Constraints

- Targets .NET 10 LTS. No earlier .NET versions.
- SQL Server is the only supported database; no multi-database abstraction.
- Email sending is decoupled via RabbitMQ; the consumer is a separate hosted service.
- The Angular frontend is out of scope for this version.
- HIPAA compliance is not a requirement for this portfolio project; security patterns are implemented as best-practice demonstrations.

### 10.2 Assumptions

- One patient can book one appointment per time slot.
- Doctors cannot have overlapping availability blocks on the same day.
- All datetimes are stored and communicated in UTC; timezone conversion is the client's responsibility.
- Email delivery is best-effort; failed emails after 3 retries go to a dead-letter queue.
- The system does not handle payments; appointment booking is free.
- A single RabbitMQ exchange and queue is sufficient for the MVP.

---

## 11. Out of Scope

The following are explicitly excluded from version 1.0 and recorded as roadmap items:

- Angular SPA frontend (separate project phase).
- In-app messaging between patient and doctor.
- Payment processing and billing.
- Electronic Health Records (EHR) integration or FHIR compliance.
- Multi-clinic / multi-tenant support.
- Video consultation (telemedicine) integration.
- Mobile application (iOS / Android).
- Doctor rating and review system.
- Automated appointment reminders (SMS or push notification).

---

## 12. Development Checklist

### Domain Layer
- [ ] `BaseEntity` abstract class (Id, CreatedAt, UpdatedAt)
- [ ] `User`, `Doctor`, `Patient` entities (TPH inheritance)
- [ ] `DoctorAvailability` entity
- [ ] `Appointment` entity with status enum and domain validation
- [ ] `AppointmentStatus`, `UserRole`, `DayOfWeek` enums
- [ ] Repository interfaces + `IUnitOfWork`
- [ ] `DomainException`, `SlotUnavailableException`

### Application Layer
- [ ] Auth handlers: Register, Login, RefreshToken
- [ ] `CreateAppointmentHandler`, `CancelAppointmentHandler`
- [ ] `GetMyAppointmentsHandler`, `GetAppointmentByIdHandler`
- [ ] `CreateAvailabilityHandler`, `DeleteAvailabilityHandler`
- [ ] `GetAvailableSlotsHandler` (with cache), `GetDoctorsHandler`
- [ ] FluentValidation validators for all commands
- [ ] DTOs for all responses
- [ ] Service interfaces: `ICacheService`, `ITokenService`, `IMessagePublisher`

### Infrastructure Layer
- [ ] `AppDbContext` with `SaveChangesAsync` audit override
- [ ] `IEntityTypeConfiguration` per entity
- [ ] Repository implementations + `UnitOfWork`
- [ ] EF Core migrations (initial schema)
- [ ] `RedisCacheService`
- [ ] `RabbitMqPublisher` + `AppointmentCreatedConsumer`
- [ ] `JwtTokenService` (issue + validate)

### API Layer
- [ ] `AuthController`, `AppointmentsController`, `DoctorsController`, `PatientsController`
- [ ] `IdempotencyMiddleware`, `RateLimitingMiddleware`, `ExceptionHandlingMiddleware`
- [ ] Swagger / OpenAPI configuration
- [ ] DI wiring in `ServiceCollectionExtensions`

### Testing
- [ ] Unit tests for handlers and slot-calculation logic
- [ ] Integration tests via `WebApplicationFactory`
- [ ] Architecture tests (NetArchTest layer rules)

### DevOps
- [ ] `Dockerfile`
- [ ] `docker-compose.yml` for local dev
- [ ] GitHub Actions: `ci.yml`, `cd-staging.yml`, `cd-production.yml`
- [ ] `README.md` with setup instructions

---

*Last updated: 2026 — Gabri Elles*
