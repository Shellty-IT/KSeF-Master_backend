# KSeF Master - Backend API

<div align="center">

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-Neon-4169E1?style=for-the-badge&logo=postgresql&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?style=for-the-badge&logo=docker&logoColor=white)
![C#](https://img.shields.io/badge/C%23-12.0-239120?style=for-the-badge&logo=csharp&logoColor=white)
![JWT](https://img.shields.io/badge/Auth-JWT-000000?style=for-the-badge&logo=jsonwebtokens&logoColor=white)
![KSeF](https://img.shields.io/badge/KSeF-API%20v2-CC0000?style=for-the-badge)

**Backend API dla KSeF Master - profesjonalna platforma fakturowania zintegrowana z Krajowym Systemem e-Faktur (KSeF API v2)**

[🌐 Aplikacja na żywo](https://ksef-master.netlify.app) · [📦 Repozytorium Frontend](https://github.com/Shellty-IT/KSeF-Master) · [📖 Dokumentacja API](#-dokumentacja-api)

> 🇬🇧 [English version](README.md)

</div>

---

## 📋 Spis treści

- [Opis projektu](#-opis-projektu)
- [Zrzuty ekranu](#-zrzuty-ekranu)
- [Architektura](#-architektura)
- [Stack technologiczny](#-stack-technologiczny)
- [Struktura projektu](#-struktura-projektu)
- [Model uwierzytelniania](#-model-uwierzytelniania)
- [Dokumentacja API](#-dokumentacja-api)
- [Schemat bazy danych](#-schemat-bazy-danych)
- [Synchronizacja faktur](#-synchronizacja-faktur)
- [Bezpieczeństwo](#-bezpieczeństwo)
- [Konfiguracja](#-konfiguracja)
- [Wdrożenie Docker](#-wdrożenie-docker)
- [Uruchomienie lokalne](#-uruchomienie-lokalne)
- [Plany rozwoju](#-plany-rozwoju)
- [Autor](#-autor)

---

## 🧭 Opis projektu

**KSeF Master Backend** to gotowy na produkcję REST API zbudowany w **.NET 8**, stanowiący rdzeń platformy fakturowania KSeF Master. Zapewnia pełną integrację z **API KSeF v2 Ministerstwa Finansów** i obsługuje:

- Dwuwarstwowe uwierzytelnianie (JWT na poziomie aplikacji + token/certyfikat KSeF)
- Bezpieczną synchronizację faktur w trybie delta z obsługą limitu 3-miesięcznych okien
- Wysyłkę faktur z pełnym wsparciem podpisywania XAdES-BES (ECDSA i RSA)
- Generowanie PDF z kodami QR
- Trwały cache faktur w PostgreSQL (Neon serverless)
- Szyfrowanie AES-256-CBC wszystkich wrażliwych danych uwierzytelniających KSeF

System zaprojektowano zgodnie z zasadami czystej architektury - Repository Pattern, Facade Pattern, ścisłe SRP i segregacja interfejsów (ISP).

---

## 📸 Zrzuty ekranu

> Zrzuty ekranu z działającej aplikacji - [https://ksef-master.netlify.app](https://ksef-master.netlify.app)

<br/>

**Dashboard - Przegląd faktur**
<!-- Dodaj zrzut ekranu tutaj -->
![Dashboard](.github/screenshots/dashboard.png)

<br/>

**Widok szczegółów faktury**
<!-- Dodaj zrzut ekranu tutaj -->
![Szczegóły faktury](.github/screenshots/invoice-details.png)

<br/>

**Konfiguracja firmy i KSeF**
<!-- Dodaj zrzut ekranu tutaj -->
![Konfiguracja firmy](.github/screenshots/company-setup.png)

<br/>

**Eksport PDF z kodem QR**
<!-- Dodaj zrzut ekranu tutaj -->
![Eksport PDF](.github/screenshots/pdf-export.png)

<br/>

**Dokumentacja API - Swagger**
<!-- Dodaj zrzut ekranu tutaj -->
![Swagger](.github/screenshots/swagger.png)

---

## 🏗 Architektura

Backend KSeF Master opiera się na **warstwowej architekturze zorientowanej na serwisy** z ścisłym podziałem odpowiedzialności.

### Warstwa 1 - Kontrolery

| Kontroler | Odpowiedzialność |
|---|---|
| `AuthController` | Uwierzytelnianie aplikacji, rejestracja użytkowników, konfiguracja firmy |
| `KSeFController` | Pełna integracja KSeF API v2 - faktury, sesje, PDF |
| `ImportController` | Import zewnętrzny (SmartQuote) |

### Warstwa 2 - Serwisy

| Grupa | Serwisy |
|---|---|
| **Auth aplikacji** | `UserAuthService`, `JwtService`, `CompanyService`, `CertificateService`, `TokenEncryptionService` |
| **KSeF Auth** | `KSeFAuthService`, `KSeFChallengeService`, `KSeFAuthPollingService`, `KSeFAuthRedeemService`, `KSeFTokenRefreshService` |
| **KSeF Certyfikat** | `KSeFCertAuthService` |
| **KSeF Faktury** | `KSeFInvoiceFacade`, `KSeFInvoiceQueryService`, `KSeFInvoiceSendService`, `KSeFInvoiceDetailsService`, `KSeFInvoiceStatsService` |
| **KSeF Sesja** | `KSeFOnlineSessionService`, `KSeFSessionManager` |
| **KSeF Wspólne** | `KSeFCryptoService`, `KSeFEnvironmentService` |
| **PDF** | `PdfGeneratorService`, `PdfDocumentComposer`, `PdfQrCodeGenerator`, `PdfSectionRenderer` |
| **Infrastruktura wspólna** | `KSeFErrorParser`, `KSeFResponseLogger`, `KSeFApiException` |

### Warstwa 3 - Repozytoria

| Interfejs | Odpowiedzialność |
|---|---|
| `IUserRepository` | Operacje CRUD na użytkownikach |
| `ICompanyRepository` | Zarządzanie profilem firmy |
| `IInvoiceRepository` | Trwałość faktur i zapytania delta sync |

> Serwisy nigdy nie dotykają `DbContext` bezpośrednio - wszystkie operacje DB przechodzą przez interfejsy repozytoriów.

### Warstwa 4 - Baza danych

PostgreSQL hostowany na **Neon (serverless)**. Tabele: `Users` · `CompanyProfiles` · `Invoices`

---

### Wzorce architektoniczne

| Wzorzec | Zastosowanie |
|---|---|
| **Repository Pattern** | Pełna abstrakcja dostępu do DB za interfejsami |
| **Facade Pattern** | `KSeFInvoiceFacade` deleguje do dedykowanych serwisów jednej odpowiedzialności |
| **SRP** | Każda klasa ma dokładnie jeden powód do zmiany |
| **ISP** | Kontrolery wstrzykują tylko interfejsy, których faktycznie używają |
| **Wspólna infrastruktura** | Jeden `KSeFErrorParser`, `KSeFResponseLogger`, `KSeFApiException` współdzielony przez wszystkie serwisy KSeF |

---

## 🛠 Stack technologiczny

| Warstwa | Technologia | Wersja |
|---|---|---|
| Runtime | .NET / ASP.NET Core | 8.0 |
| Język | C# | 12.0 |
| ORM | Entity Framework Core | 8.0 |
| Baza danych | PostgreSQL - Neon serverless | - |
| Driver DB | Npgsql.EntityFrameworkCore.PostgreSQL | 8.0.4 |
| Uwierzytelnianie | JWT Bearer | 8.0.0 |
| Haszowanie haseł | BCrypt.Net-Next | 4.0.3 |
| Szyfrowanie | AES-256-CBC (wbudowane .NET) | - |
| Podpisywanie faktur | XAdES-BES - ECDSA + RSA | - |
| Walidacja | FluentValidation.AspNetCore | 11.3.0 |
| Generowanie PDF | QuestPDF | 2024.3.0 |
| Kody QR | QRCoder | 1.6.0 |
| Dokumentacja API | Swashbuckle / Swagger | 6.5.0 |
| Konteneryzacja | Docker - Linux | - |
| Wdrożenie | Render.com | - |

---

## 📁 Struktura projektu

- **Controllers/** - punkty wejścia HTTP
    - `AuthController.cs` - auth aplikacji i konfiguracja firmy
    - `KSeFController.cs` - integracja KSeF API
    - `ImportController.cs` - import zewnętrzny

- **Infrastructure/**
    - **Extensions/** - rozszerzenia startowe DI (Auth, DB, CORS, Swagger, klienty HTTP, serwisy)
    - **KSeF/** - wspólna infrastruktura KSeF (`KSeFApiException`, `KSeFErrorParser`, `KSeFResponseLogger`, `KSeFHttpLoggingHandler`)

- **Models/**
    - **Data/** - encje EF Core (`User`, `CompanyProfile`, `Invoice`, `AppDbContext`)
    - **Requests/** - przychodzące DTO
    - **Responses/** - wychodzące DTO (`Auth/`, `Invoice/`, `Certificate/`, `Stats/`, `Common/`)

- **Repositories/**
    - `IUserRepository` / `UserRepository`
    - `ICompanyRepository` / `CompanyRepository`
    - `IInvoiceRepository` / `InvoiceRepository`

- **Services/**
    - **Auth/** - `UserAuthService`, `JwtService`, `CompanyService`, `CertificateService`, `TokenEncryptionService`
    - **KSeF/**
        - **Auth/** - challenge, polling, redeem, odświeżanie tokenu
        - **Certificate/** - uwierzytelnianie certyfikatem XAdES-BES
        - **Common/** - kryptografia, środowisko, wspólny auth
        - **Invoice/** - facade, zapytania, wysyłka, szczegóły, statystyki, sesja online
        - **Session/** - `KSeFSessionManager`
    - **Pdf/** - `PdfGeneratorService`, `PdfDocumentComposer`, `PdfQrCodeGenerator`, `PdfSectionRenderer`, `PdfUrlBuilder`
    - **Invoice/** - `InvoiceXmlGenerator`
    - **External/** - `ExternalDraftService` (SmartQuote)

- **Validators/** - walidatory FluentValidation dla wszystkich żądań
- **Mappers/** - mapery encja ↔ DTO
- **Migrations/** - migracje bazy danych EF Core
- `Program.cs` - punkt wejścia aplikacji i konfiguracja pipeline'u
- `appsettings.json` - bazowa konfiguracja
- `Dockerfile` - wieloetapowy build Docker

---

## 🔐 Model uwierzytelniania

### Warstwa 1 - Uwierzytelnianie aplikacji

- Rejestracja i logowanie przez email + hasło
- Hasła haszowane przez **BCrypt**
- Sesje zarządzane przez **tokeny JWT Bearer** (wygasanie po 24h)
- Użytkownik może korzystać z aplikacji bez podłączonego KSeF

### Warstwa 2 - Uwierzytelnianie KSeF

Konfiguracja per firma, w pełni niezależna od warstwy logowania aplikacji.

| Metoda | Szczegóły |
|---|---|
| **Token** | Token dostępowy KSeF, szyfrowany AES-256-CBC w bazie |
| **Certyfikat** | Format PEM (ECDSA lub RSA), podpisywanie XAdES-BES, numer seryjny w postaci dziesiętnej |

Przepływ uwierzytelniania KSeF: **challenge → podpisanie → realizacja → token dostępowy**

Wszystkie wrażliwe dane (tokeny, certyfikaty, klucze prywatne, hasła) są przechowywane **zaszyfrowane AES-256-CBC** w PostgreSQL.

---

## 📡 Dokumentacja API

Wszystkie odpowiedzi używają ujednoliconego formatu:

```json
{
  "success": true,
  "data": {},
  "message": "Operacja zakończona pomyślnie",
  "error": null
}
Auth - /api/auth
Zarządza użytkownikami aplikacji, profilami firm i danymi uwierzytelniającymi KSeF.

Metoda	Endpoint	Dostęp	Opis
POST	/api/auth/register	Publiczny	Utwórz nowe konto użytkownika
POST	/api/auth/login	Publiczny	Zaloguj się i otrzymaj JWT
GET	/api/auth/status	JWT	Pobierz pełny status uwierzytelnienia
POST	/api/auth/company/setup	JWT	Skonfiguruj nazwę firmy i NIP
POST	/api/auth/ksef/connect	JWT	Zapisz i aktywuj token KSeF
POST	/api/auth/ksef/disconnect	JWT	Usuń aktywny token KSeF
POST	/api/auth/ksef/environment	JWT	Przełącz między Test i Produkcja
POST	/api/auth/certificate/upload	JWT	Wgraj certyfikat PEM
GET	/api/auth/certificate/info	JWT	Pobierz metadane certyfikatu
DELETE	/api/auth/certificate	JWT	Usuń zapisany certyfikat
KSeF - /api/ksef
Obsługuje zarządzanie sesją KSeF, operacje na fakturach i generowanie PDF.

Sesja i status

Metoda	Endpoint	Dostęp	Opis
GET	/api/ksef/status	Publiczny	Stan serwera i informacje o sesji
POST	/api/ksef/login	JWT	Uwierzytelnij się w KSeF API
POST	/api/ksef/logout	JWT	Zakończ sesję KSeF
POST	/api/ksef/session/open	JWT	Otwórz sesję wysyłkową online
POST	/api/ksef/session/close	JWT	Zamknij sesję online
POST	/api/ksef/session/close-and-upo	JWT	Zamknij sesję i pobierz UPO
Faktury

Metoda	Endpoint	Dostęp	Opis
GET	/api/ksef/invoices/cached	JWT	Zwróć faktury z lokalnego cache DB
POST	/api/ksef/invoices/sync	JWT	Wymuś pełną synchronizację delta
POST	/api/ksef/invoices	JWT	Synchronizuj i zwróć wszystkie faktury
GET	/api/ksef/invoices/stats	JWT	Zagregowane statystyki faktur
GET	/api/ksef/invoice/{ksefNumber}	JWT	Pobierz szczegóły jednej faktury
POST	/api/ksef/invoice/send	JWT	Wyślij nową fakturę do KSeF
POST	/api/ksef/invoice/pdf	JWT	Wygeneruj PDF z osadzonym kodem QR
Środowiska KSeF
Środowisko	Base URL
Test	https://api-test.ksef.mf.gov.pl/v2/
Produkcja	https://api.ksef.mf.gov.pl/v2/
🗄 Schemat bazy danych
PostgreSQL hostowany na Neon (serverless). Schemat zarządzany przez migracje EF Core - aplikowane automatycznie przy starcie.

Users
Kolumna	Typ	Uwagi
Id	int	Klucz główny
Email	string	Unikalny
PasswordHash	string	BCrypt
Name	string	-
CreatedAt	datetime	-
CompanyProfiles
Kolumna	Typ	Uwagi
Id	int	Klucz główny
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
Kolumna	Typ	Uwagi
Id	int	Klucz główny
CompanyProfileId	int	FK → CompanyProfiles (1:N)
KsefReferenceNumber	string	Unikalny - klucz deduplikacji
Direction	string	issued / received
InvoiceNumber	string	-
SellerNip / SellerName	string	-
BuyerNip / BuyerName	string	-
NetAmount / VatAmount / GrossAmount	decimal	-
Currency	string	-
InvoiceDate	datetime	-
AcquisitionTimestamp	datetime	Używany do delta sync
SyncedAt	datetime	-
XmlContent	string	Surowy XML KSeF
KsefEnvironment	string	Test / Production
Automatyczne zachowania:

Faktury kasowane automatycznie przy zmianie NIP firmy
KsefReferenceNumber zapobiega duplikatom między synchronizacjami
Migracje uruchamiają się automatycznie przy każdym starcie aplikacji
🔄 Synchronizacja faktur
Silnik synchronizacji obsługuje transparentnie limit 3-miesięcznego okna zapytań KSeF.

Jak to działa
Odczyt ostatniego AcquisitionTimestamp zapisanego w bazie danych
Podział zakresu czasu od tego momentu do teraz na okna 3-miesięczne
Dla każdego okna - zapytanie KSeF o faktury wystawione, następnie otrzymane
Zapis tylko faktur z KsefReferenceNumber jeszcze nieobecnym w bazie
Zwrot { newCount, totalFetched } per kierunek
Gwarancje synchronizacji
Gwarancja	Szczegół
Synchronizacja delta	Tylko nowe faktury są pobierane przy każdym uruchomieniu
Obsługa okien	Limit 3-miesięczny KSeF jest automatycznie dzielony i zarządzany
Deduplikacja	Wymuszana przez unikalny KsefReferenceNumber
Bezpieczeństwo zmiany NIP	Wszystkie faktury z cache są czyszczone przy zmianie NIP
Izolacja środowisk	Faktury Test i Produkcja przechowywane oddzielnie
🔒 Bezpieczeństwo
Obszar	Implementacja
Przechowywanie haseł	BCrypt - hash z solą
Token KSeF w bazie	Szyfrowanie AES-256-CBC w PostgreSQL
Certyfikat w bazie	Szyfrowanie AES-256-CBC w PostgreSQL
Klucz prywatny w bazie	Szyfrowanie AES-256-CBC w PostgreSQL
Sesja aplikacji	JWT Bearer - 24h ważność, podpisany HMAC
Podpisywanie faktur	XAdES-BES - obsługa ECDSA i RSA
Zarządzanie sekretami	Wszystkie sekrety przez zmienne środowiskowe - nigdy nie commitowane
Higiena Git	appsettings.Development.json jest w .gitignore
⚙️ Konfiguracja
Struktura appsettings.json - bez sekretów, tylko szkielet:

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
Wymagane zmienne środowiskowe
Zmienna	Opis
ConnectionStrings__DefaultConnection	Connection string Neon PostgreSQL
Jwt__Key	Klucz podpisywania JWT - minimum 32 znaki
Encryption__Key	Klucz szyfrowania AES-256
PORT	Port HTTP - domyślnie 8080
🐳 Wdrożenie Docker
Wieloetapowy build - pełne SDK do budowania, minimalny runtime ASP.NET dla obrazu produkcyjnego.

Bash

# Build
docker build -t ksef-backend .

# Uruchom
docker run -p 8080:8080 `
  -e ConnectionStrings__DefaultConnection="twoj-connection-string-neon" `
  -e Jwt__Key="twoj-tajny-klucz-jwt" `
  -e Encryption__Key="twoj-klucz-szyfrowania" `
  ksef-backend
Wdrożenie produkcyjne: Render.com - auto-deploy z obrazu Docker.

🚀 Uruchomienie lokalne
Aplikacja działa już na żywo pod adresem https://ksef-master.netlify.app

Poniższe kroki dotyczą lokalnego środowiska deweloperskiego.

Wymagania
.NET 8 SDK
PostgreSQL lub konto Neon
Docker (opcjonalnie)
Kroki
1. Klonowanie

Bash

git clone https://github.com/Shellty-IT/KSeF-Master_backend.git
cd KSeF-Master_backend
2. Utwórz appsettings.Development.json

JSON

{
  "ConnectionStrings": {
    "DefaultConnection": "Host=...;Database=ksef_master;Username=...;Password=..."
  },
  "Jwt": {
    "Key": "lokalny-klucz-deweloperski-minimum-32-znaki"
  },
  "Encryption": {
    "Key": "lokalny-klucz-szyfrowania-32-znaki"
  }
}
3. Uruchom

Bash

dotnet run
Migracje są aplikowane automatycznie przy starcie.

4. Otwórz Swagger



http://localhost:8080/swagger
🗺 Plany rozwoju
 Pełna integracja KSeF API v2
 Dwuwarstwowe uwierzytelnianie JWT
 Trwałość danych PostgreSQL - Neon
 Synchronizacja delta faktur
 Podpisywanie certyfikatem XAdES-BES
 Generowanie PDF z kodami QR
 Wdrożenie Docker na Render.com
 Testy jednostkowe i integracyjne
 Moduł wykrywania nadużyć (fraud detection)
 Baza danych kontrahentów
 Integracja SmartQuote
 Separacja gałęzi produkcyjnej i stagingowej Neon
👤 Autor
Shellty

Backend zbudowany z precyzją, zaprojektowany z myślą o produkcji.


© 2026 Shellty