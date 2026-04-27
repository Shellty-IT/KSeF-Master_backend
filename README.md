# KSeF Master - Backend API

<div align="center">

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-Neon-4169E1?style=for-the-badge&logo=postgresql&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?style=for-the-badge&logo=docker&logoColor=white)
![C#](https://img.shields.io/badge/C%23-12.0-239120?style=for-the-badge&logo=csharp&logoColor=white)
![JWT](https://img.shields.io/badge/Auth-JWT-000000?style=for-the-badge&logo=jsonwebtokens&logoColor=white)
![KSeF](https://img.shields.io/badge/KSeF-API%20v2-CC0000?style=for-the-badge)

**Backend API for KSeF Master - a professional invoicing platform integrating with the Polish National e-Invoice System (KSeF API v2)**

[🌐 Live Application](https://ksef-master.netlify.app) · [📦 Frontend Repository](https://github.com/Shellty-IT/KSeF-Master) · [📖 API Docs](#-api-reference)

> 🇵🇱 [Polska wersja README](README.pl.md)

</div>

---

## 📋 Table of Contents

- [Overview](#-overview)
- [Screenshots](#-screenshots)
- [Architecture](#-architecture)
- [Tech Stack](#-tech-stack)
- [Project Structure](#-project-structure)
- [Authentication Model](#-authentication-model)
- [API Reference](#-api-reference)
- [Database Schema](#-database-schema)
- [Invoice Synchronization](#-invoice-synchronization)
- [Security](#-security)
- [Configuration](#-configuration)
- [Docker Deployment](#-docker-deployment)
- [Getting Started](#-getting-started)
- [Roadmap](#-roadmap)
- [Author](#-author)

---

## 🧭 Overview

**KSeF Master Backend** is a production-ready REST API built with **.NET 8**, serving as the backbone of the KSeF Master invoicing platform. It provides full integration with the **Polish Ministry of Finance KSeF API v2**, handling:

- Two-layer authentication (app-level JWT + KSeF-level token/certificate)
- Secure invoice synchronization with delta strategy and 3-month window handling
- Invoice sending with full XAdES-BES signing support (ECDSA & RSA)
- PDF generation with QR codes
- Persistent invoice cache in PostgreSQL (Neon serverless)
- AES-256-CBC encryption for all sensitive KSeF credentials

The system is designed with clean architecture principles - Repository Pattern, Facade Pattern, strict SRP, and interface-segregation throughout.

---

## 📸 Screenshots

> Screenshots of the live application - [https://ksef-master.netlify.app](https://ksef-master.netlify.app)

<br/>

**Dashboard - Invoice Overview**
<!-- Add screenshot here -->
![Dashboard](.github/screenshots/dashboard.png)

<br/>

**Invoice Details View**
<!-- Add screenshot here -->
![Invoice Details](.github/screenshots/invoice-details.png)

<br/>

**Company & KSeF Configuration**
<!-- Add screenshot here -->
![Company Setup](.github/screenshots/company-setup.png)

<br/>

**PDF Export with QR Code**
<!-- Add screenshot here -->
![PDF Export](.github/screenshots/pdf-export.png)

<br/>

**Swagger API Documentation**
<!-- Add screenshot here -->
![Swagger](.github/screenshots/swagger.png)

---

## 🏗 Architecture

KSeF Master Backend follows a **layered, service-oriented architecture** with strict separation of concerns.

### Layer 1 - Controllers

| Controller | Responsibility |
|---|---|
| `AuthController` | App authentication, user registration, company configuration |
| `KSeFController` | Full KSeF API v2 integration - invoices, sessions, PDF |
| `ImportController` | External import (SmartQuote) |

### Layer 2 - Services

| Group | Services |
|---|---|
| **App Auth** | `UserAuthService`, `JwtService`, `CompanyService`, `CertificateService`, `TokenEncryptionService` |
| **KSeF Auth** | `KSeFAuthService`, `KSeFChallengeService`, `KSeFAuthPollingService`, `KSeFAuthRedeemService`, `KSeFTokenRefreshService` |
| **KSeF Certificate** | `KSeFCertAuthService` |
| **KSeF Invoice** | `KSeFInvoiceFacade`, `KSeFInvoiceQueryService`, `KSeFInvoiceSendService`, `KSeFInvoiceDetailsService`, `KSeFInvoiceStatsService` |
| **KSeF Session** | `KSeFOnlineSessionService`, `KSeFSessionManager` |
| **KSeF Common** | `KSeFCryptoService`, `KSeFEnvironmentService` |
| **PDF** | `PdfGeneratorService`, `PdfDocumentComposer`, `PdfQrCodeGenerator`, `PdfSectionRenderer` |
| **Shared Infra** | `KSeFErrorParser`, `KSeFResponseLogger`, `KSeFApiException` |

### Layer 3 - Repositories

| Interface | Responsibility |
|---|---|
| `IUserRepository` | User CRUD operations |
| `ICompanyRepository` | Company profile management |
| `IInvoiceRepository` | Invoice persistence and delta sync queries |

> Services never access `DbContext` directly - all database operations go through repository interfaces.

### Layer 4 - Database

PostgreSQL hosted on **Neon (serverless)**. Tables: `Users` · `CompanyProfiles` · `Invoices`

---

### Architectural Patterns

| Pattern | How it is applied |
|---|---|
| **Repository Pattern** | Full abstraction of all DB access behind interfaces |
| **Facade Pattern** | `KSeFInvoiceFacade` delegates to dedicated single-responsibility services |
| **SRP** | Every class has exactly one reason to change |
| **ISP** | Controllers inject only the interfaces they actually use |
| **Shared Infrastructure** | Single `KSeFErrorParser`, `KSeFResponseLogger`, `KSeFApiException` shared across all KSeF services |

---

## 🛠 Tech Stack

| Layer | Technology | Version |
|---|---|---|
| Runtime | .NET / ASP.NET Core | 8.0 |
| Language | C# | 12.0 |
| ORM | Entity Framework Core | 8.0 |
| Database | PostgreSQL - Neon serverless | - |
| DB Driver | Npgsql.EntityFrameworkCore.PostgreSQL | 8.0.4 |
| Authentication | JWT Bearer | 8.0.0 |
| Password Hashing | BCrypt.Net-Next | 4.0.3 |
| Encryption | AES-256-CBC (built-in .NET) | - |
| Invoice Signing | XAdES-BES - ECDSA + RSA | - |
| Validation | FluentValidation.AspNetCore | 11.3.0 |
| PDF Generation | QuestPDF | 2024.3.0 |
| QR Codes | QRCoder | 1.6.0 |
| API Docs | Swashbuckle / Swagger | 6.5.0 |
| Containerization | Docker - Linux | - |
| Deployment | Render.com | - |

---

## 📁 Project Structure

- **Controllers/** - HTTP entry points
    - `AuthController.cs` - app auth & company setup
    - `KSeFController.cs` - KSeF API integration
    - `ImportController.cs` - external import

- **Infrastructure/**
    - **Extensions/** - startup DI extensions (Auth, DB, CORS, Swagger, HTTP clients, Services)
    - **KSeF/** - shared KSeF infrastructure (`KSeFApiException`, `KSeFErrorParser`, `KSeFResponseLogger`, `KSeFHttpLoggingHandler`)

- **Models/**
    - **Data/** - EF Core entities (`User`, `CompanyProfile`, `Invoice`, `AppDbContext`)
    - **Requests/** - incoming DTOs
    - **Responses/** - outgoing DTOs (`Auth/`, `Invoice/`, `Certificate/`, `Stats/`, `Common/`)

- **Repositories/**
    - `IUserRepository` / `UserRepository`
    - `ICompanyRepository` / `CompanyRepository`
    - `IInvoiceRepository` / `InvoiceRepository`

- **Services/**
    - **Auth/** - `UserAuthService`, `JwtService`, `CompanyService`, `CertificateService`, `TokenEncryptionService`
    - **KSeF/**
        - **Auth/** - challenge, polling, redeem, token refresh
        - **Certificate/** - XAdES-BES certificate authentication
        - **Common/** - crypto, environment, shared auth
        - **Invoice/** - facade, query, send, details, stats, online session
        - **Session/** - `KSeFSessionManager`
    - **Pdf/** - `PdfGeneratorService`, `PdfDocumentComposer`, `PdfQrCodeGenerator`, `PdfSectionRenderer`, `PdfUrlBuilder`
    - **Invoice/** - `InvoiceXmlGenerator`
    - **External/** - `ExternalDraftService` (SmartQuote)

- **Validators/** - FluentValidation validators for all requests
- **Mappers/** - entity ↔ DTO mappers
- **Migrations/** - EF Core database migrations
- `Program.cs` - application entry point & pipeline
- `appsettings.json` - base configuration
- `Dockerfile` - multi-stage Docker build

---

## 🔐 Authentication Model

### Layer 1 - Application Authentication

- Email + password registration and login
- Passwords hashed with **BCrypt**
- Sessions managed via **JWT Bearer tokens** (24-hour expiry)
- Users can access the application without KSeF connected

### Layer 2 - KSeF Authentication

Per-company configuration, fully independent from the app login layer.

| Method | Details |
|---|---|
| **Token** | KSeF-issued access token, encrypted with AES-256-CBC at rest |
| **Certificate** | PEM format (ECDSA or RSA), XAdES-BES signing, serial number in decimal format |

KSeF authentication flow: **challenge → sign → redeem → access token**

All sensitive data (tokens, certificates, private keys, passwords) are stored **AES-256-CBC encrypted** in PostgreSQL.

---

## 📡 API Reference

All responses use a unified envelope format:

```json
{
  "success": true,
  "data": {},
  "message": "Operation completed",
  "error": null
}
Auth - /api/auth
Manages application users, company profiles and KSeF credentials.

Method	Endpoint	Access	Description
POST	/api/auth/register	Public	Create a new user account
POST	/api/auth/login	Public	Authenticate and receive a JWT
GET	/api/auth/status	JWT	Get full authentication status
POST	/api/auth/company/setup	JWT	Configure company name and NIP
POST	/api/auth/ksef/connect	JWT	Save and activate a KSeF token
POST	/api/auth/ksef/disconnect	JWT	Remove active KSeF token
POST	/api/auth/ksef/environment	JWT	Switch between Test and Production
POST	/api/auth/certificate/upload	JWT	Upload a PEM certificate
GET	/api/auth/certificate/info	JWT	Get certificate metadata
DELETE	/api/auth/certificate	JWT	Delete stored certificate
KSeF - /api/ksef
Handles KSeF session management, invoice operations and PDF generation.

Session & Status

Method	Endpoint	Access	Description
GET	/api/ksef/status	Public	Server health and session info
POST	/api/ksef/login	JWT	Authenticate to KSeF API
POST	/api/ksef/logout	JWT	End KSeF session
POST	/api/ksef/session/open	JWT	Open an online send session
POST	/api/ksef/session/close	JWT	Close the online session
POST	/api/ksef/session/close-and-upo	JWT	Close session and retrieve UPO
Invoices

Method	Endpoint	Access	Description
GET	/api/ksef/invoices/cached	JWT	Return invoices from local DB cache
POST	/api/ksef/invoices/sync	JWT	Force a full delta synchronization
POST	/api/ksef/invoices	JWT	Sync then return all invoices
GET	/api/ksef/invoices/stats	JWT	Aggregated invoice statistics
GET	/api/ksef/invoice/{ksefNumber}	JWT	Fetch details of a single invoice
POST	/api/ksef/invoice/send	JWT	Send a new invoice to KSeF
POST	/api/ksef/invoice/pdf	JWT	Generate a PDF with embedded QR code
KSeF Environments
Environment	Base URL
Test	https://api-test.ksef.mf.gov.pl/v2/
Production	https://api.ksef.mf.gov.pl/v2/
🗄 Database Schema
PostgreSQL hosted on Neon (serverless). Schema managed via EF Core migrations - applied automatically on startup.

Users
Column	Type	Notes
Id	int	Primary key
Email	string	Unique
PasswordHash	string	BCrypt
Name	string	-
CreatedAt	datetime	-
CompanyProfiles
Column	Type	Notes
Id	int	Primary key
UserId	int	FK → Users (1:1)
CompanyName	string	-
Nip	string	-
KsefTokenEncrypted	string	AES-256-CBC
AuthMethod	string	Token / Certificate
KsefEnvironment	string	Test / Production
CertificateEncrypted	string	AES-256-CBC
PrivateKeyEncrypted	string	AES-256-CBC
CertificatePasswordEncrypted	string	AES-256-CBC
IsActive	bool	-
CreatedAt / UpdatedAt	datetime	-
Invoices
Column	Type	Notes
Id	int	Primary key
CompanyProfileId	int	FK → CompanyProfiles (1:N)
KsefReferenceNumber	string	Unique - deduplication key
Direction	string	issued / received
InvoiceNumber	string	-
SellerNip / SellerName	string	-
BuyerNip / BuyerName	string	-
NetAmount / VatAmount / GrossAmount	decimal	-
Currency	string	-
InvoiceDate	datetime	-
AcquisitionTimestamp	datetime	Used for delta sync
SyncedAt	datetime	-
XmlContent	string	Raw KSeF XML
KsefEnvironment	string	Test / Production
Automatic behaviors:

Invoices are deleted automatically when company NIP changes
KsefReferenceNumber prevents duplicates across sync runs
Migrations run automatically on every application startup
🔄 Invoice Synchronization
The sync engine transparently handles KSeF's 3-month query window limitation.

How it works
Read the latest AcquisitionTimestamp stored in the database
Split the time range from that point to now into 3-month windows
For each window - query KSeF for issued invoices, then received invoices
Persist only invoices with a KsefReferenceNumber not already in the database
Return { newCount, totalFetched } per direction
Sync guarantees
Guarantee	Detail
Delta sync	Only new invoices are downloaded on each run
Window handling	3-month KSeF limit is split and managed automatically
Deduplication	Enforced by unique KsefReferenceNumber
NIP change safety	All cached invoices are cleared when NIP changes
Environment isolation	Test and Production invoices are stored separately
🔒 Security
Area	Implementation
Password storage	BCrypt - salted hash
KSeF token at rest	AES-256-CBC encryption in PostgreSQL
Certificate at rest	AES-256-CBC encryption in PostgreSQL
Private key at rest	AES-256-CBC encryption in PostgreSQL
App session	JWT Bearer - 24-hour expiry, HMAC-signed
Invoice signing	XAdES-BES - supports both ECDSA and RSA
Secrets management	All secrets via environment variables - never committed
Git hygiene	appsettings.Development.json is gitignored
⚙️ Configuration
appsettings.json structure - no secrets, skeleton only:

JSON

{
  "KSeF": {
    "DefaultEnvironment": "Test",
    "TimeoutSeconds": 60,
    "Environments": {
      "Test": {
        "ApiBaseUrl": "https://api-test.ksef.mf.gov.pl/v2/",
        "AppUrl": "https://ap-test.ksef.mf.gov.pl",
        "QrBaseUrl": "https://qr-test.ksef.mf.gov.pl"
      },
      "Production": {
        "ApiBaseUrl": "https://api.ksef.mf.gov.pl/v2/",
        "AppUrl": "https://ap.ksef.mf.gov.pl",
        "QrBaseUrl": "https://qr.ksef.mf.gov.pl"
      }
    }
  },
  "Jwt": {
    "Issuer": "KSeFMaster",
    "Audience": "KSeFMasterApp",
    "ExpirationHours": 24
  }
}
Required Environment Variables
Variable	Description
ConnectionStrings__DefaultConnection	Neon PostgreSQL connection string
Jwt__Key	JWT signing key - minimum 32 characters
Encryption__Key	AES-256 encryption key
PORT	HTTP port - default 8080
🐳 Docker Deployment
Multi-stage build - full SDK for build, minimal ASP.NET runtime for production image.

Bash

# Build
docker build -t ksef-backend .

# Run
docker run -p 8080:8080 `
  -e ConnectionStrings__DefaultConnection="your-neon-connection-string" `
  -e Jwt__Key="your-jwt-secret-key" `
  -e Encryption__Key="your-encryption-key" `
  ksef-backend
Production deployment: Render.com - auto-deploy from Docker image.

🚀 Getting Started
The application is live at https://ksef-master.netlify.app

Follow these steps for local development.

Prerequisites
.NET 8 SDK
PostgreSQL or a Neon account
Docker (optional)
Steps
1. Clone

Bash

git clone https://github.com/Shellty-IT/KSeF-Master_backend.git
cd KSeF-Master_backend
2. Create appsettings.Development.json

JSON

{
  "ConnectionStrings": {
    "DefaultConnection": "Host=...;Database=ksef_master;Username=...;Password=..."
  },
  "Jwt": {
    "Key": "local-dev-secret-key-minimum-32-characters"
  },
  "Encryption": {
    "Key": "local-encryption-key-32-characters"
  }
}
3. Run

Bash

dotnet run
Migrations are applied automatically on startup.

4. Open Swagger

http://localhost:8080/swagger
🗺 Roadmap
 Full KSeF API v2 integration
 JWT two-layer authentication
 PostgreSQL persistence - Neon
 Delta invoice synchronization
 XAdES-BES certificate signing
 PDF generation with QR codes
 Docker deployment on Render.com
 Unit and integration tests
 Fraud detection module
 Contractor database
 SmartQuote integration
 Production / staging Neon branch separation
👤 Author
Shellty

Backend built with precision, designed for production.


© 2026 Shellty

