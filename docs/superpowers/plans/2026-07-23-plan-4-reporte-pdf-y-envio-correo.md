# OC Automática — Plan 4: Reporte PDF de la OC y envío de copia por correo

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** El comprador puede abrir "Visualizar OC" en la pantalla "OCs por proveedor" y ver un PDF (generado con QuestPDF, equivalente al reporte de Crystal actual) con el detalle completo de la orden, y puede usar "Enviar OC por email al usuario" para recibir una copia de esa OC en su propio correo (resuelto dinámicamente desde Epicor), encolada en `interfaz_envia_oc` con parámetros tipados — nunca con SQL concatenado.

**Architecture:** Un `PurchaseOrderReportService` nuevo junta, con llamadas OData directas (mismo patrón que el resto del proyecto — nunca BAQs custom ni SQL directo a Epicor), todos los datos que antes traía el query embebido del `.rpt` de Crystal, y un `PurchaseOrderPdfDocument` (QuestPDF) los convierte en PDF. Un `PurchaseOrderEmailService` separado resuelve el correo del usuario logueado (`Ice.BO.UserFileSvc`) y lo inserta directo en `interfaz_envia_oc` en el servidor `192.168.100.18` a través de un `IEmailQueueRepository` con `Microsoft.Data.SqlClient` y parámetros tipados — sin pasar nunca por el SP `sp_EnviaOC_V2` ni por el SQL Server de Epicor.

**Tech Stack:** .NET 8 y React 19 + TypeScript ya en uso. Librerías nuevas: **QuestPDF** (licencia Community) y **Microsoft.Data.SqlClient**.

## Global Constraints

- **.NET 8** (`net8.0`). No usar preview features.
- **Epicor REST v2** únicamente para todo lo que sea dato de Epicor. Ninguna conexión SQL directa al servidor de Epicor (`SRVCSJPR2`).
- La única conexión SQL directa permitida en este plan es a **`192.168.100.18` / `CFSJService`**, y solo para insertar en `interfaz_envia_oc` — **siempre con parámetros tipados** (`SqlParameter`), nunca concatenación de texto.
- **Ninguna credencial en código ni en `appsettings.json` versionado** — la cadena de conexión real vive en `appsettings.Development.json` (gitignored, mismo patrón que `Epicor:ApiKey`).
- **El navegador nunca recibe credenciales de Epicor ni la cadena de conexión SQL.**
- **NO usar FluentAssertions ni librerías de mocking.** Solo asserts de xUnit y stubs/fakes escritos a mano (mismo patrón que `StubEpicorClient` en los planes anteriores) — este proyecto no tiene Moq ni ninguna librería equivalente instalada.
- Ambiente de desarrollo: **el Epicor de pruebas** (`CFSJ_LAF`), nunca producción.
- Mensajes de la interfaz en **español**; código y comentarios en **inglés**.
- Cantidades y costos son **decimales** en todo el flujo — nunca truncados a `int`.
- Toda llamada a `IEpicorClient` con un valor interpolado en la URL debe escapar ese valor con `Uri.EscapeDataString` (y doblar comillas simples con `.Replace("'", "''")` antes de escapar, si el valor puede contener una) — patrón ya establecido en `VendorService`, `AuthService`, `PurchaseOrderHistoryService`.
- La deserialización de `EpicorClient` usa `PropertyNameCaseInsensitive = true` — los nombres de propiedad de los DTOs nuevos no necesitan coincidir en mayúsculas/minúsculas exactas con el JSON real de Epicor, solo el nombre.
- Seguir los patrones ya establecidos: servicios con `IEpicorClient` (y lo que corresponda) inyectado; controladores que verifican `HttpContext.Items[SessionMiddleware.ItemKey]`, resuelven credenciales con `_sessions.GetCredentials(session.SessionId)`, y atrapan `EpicorException` con el `HandleEpicorException` ya existente en `PurchaseOrdersController`.
- **Fuera de alcance de este plan** (ver spec, sección 1): el envío automático al proveedor (`oc_tipo = 1`) al crear la OC, y cualquier UI para agregar correos manualmente. No tocar `PurchaseOrderPanel.tsx` ni el flujo de "Procesar orden de compra".

---

## Contexto: interfaces ya existentes (no se repiten, solo se consumen)

- `IEpicorClient.GetAsync<T>(company, relativePath, credentials, ct)` — `src/OCAutomatica.Api/Epicor/`. Ya soporta `InvokeFunctionAsync` (Plan 3) — este plan **no** lo necesita, solo `GetAsync`.
- `EpicorException { int StatusCode; EpicorErrorReason Reason; string Message; }`, `EpicorCredentials(string Username, string Password)`.
- `EpicorOptions { string BaseUrl; string ApiKey; string AnchorCompany; }` — `AnchorCompany` ya se usa para llamadas company-agnósticas a `Ice.BO.UserFileSvc` (ver `AuthService`).
- `ISessionStore.GetCredentials(string sessionId)` → `EpicorCredentials?`.
- `UserSession { string SessionId; string Username; string Company; string Plant; string? BuyerId; IReadOnlyList<CompanyAccess> AvailableCompanies; }` — `CompanyAccess(string Company, string CompanyName)`.
- `SessionMiddleware.ItemKey`.
- `IOrganizationService.GetPlantsAsync(company, credentials, ct)` → `IReadOnlyList<Plant>`, `Plant(string PlantId, string Name)` — `src/OCAutomatica.Api/Organization/`.
- `IPurchaseOrderHistoryService.GetDetailAsync(company, poNum, credentials, ct)` — ya existe, se usa en la pantalla "OCs por proveedor" para el grid de líneas por release. Este plan **no lo reutiliza** para el PDF: el PDF necesita una fila por **línea de OC** (no por release), así que trae sus propias líneas desde `PODetails` — ver Task 4.
- `PODetailDto { int PONUM; int POLine; string PartNum; string LineDesc; decimal OrderQty; string IUM; decimal UnitCost; }` — `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderHistoryModels.cs`. Se reutiliza tal cual en el DTO nuevo del Task 4.
- `PurchaseOrdersController` ya existe con `_sessions`, `_logger`, y el helper privado `HandleEpicorException(EpicorException ex)` (mapea `InvalidApiKey`→503, `AccessDenied`→503, todo lo demás→422 con el mensaje real de Epicor). Este plan **reutiliza ese mismo helper**, no escribe uno nuevo.
- Frontend: `src/web/src/api/client.ts` exporta `api = { ..., purchaseOrders: { create, byVendor, lines } }`, `ApiError { status }`. `src/web/src/purchaseOrders/PurchaseOrderHistory.tsx` ya tiene los botones "Visualizar OC" y "Enviar OC por email al usuario" **deshabilitados** con un `title` explicando que esperan este plan.

---

## Estructura de archivos

```
src/OCAutomatica.Api/
├── OCAutomatica.Api.csproj            # (modificar: + QuestPDF, + Microsoft.Data.SqlClient)
├── appsettings.json                    # (modificar: + seccion EmailQueue)
├── Program.cs                          # (modificar: DI + licencia QuestPDF)
├── Users/
│   ├── IUserDirectoryService.cs        # (nuevo)
│   └── UserDirectoryService.cs         # (nuevo)
├── PurchaseOrders/
│   ├── EmailQueueOptions.cs            # (nuevo)
│   ├── IEmailQueueRepository.cs        # (nuevo)
│   ├── EmailQueueRepository.cs         # (nuevo)
│   ├── InvalidUserEmailException.cs    # (nuevo)
│   ├── IPurchaseOrderEmailService.cs   # (nuevo)
│   ├── PurchaseOrderEmailService.cs    # (nuevo)
│   ├── PurchaseOrderReportModels.cs    # (nuevo)
│   ├── IPurchaseOrderReportService.cs  # (nuevo)
│   ├── PurchaseOrderReportService.cs   # (nuevo)
│   └── PurchaseOrderPdfDocument.cs     # (nuevo)
└── Controllers/
    └── PurchaseOrdersController.cs     # (modificar: + GetReport, + SendCopy)

src/web/src/
├── api/client.ts                       # (modificar: + api.purchaseOrders.reportUrl/sendCopy)
└── purchaseOrders/
    └── PurchaseOrderHistory.tsx        # (modificar: habilitar los dos botones)

tests/OCAutomatica.Api.Tests/
├── Users/UserDirectoryServiceTests.cs           # (nuevo)
└── PurchaseOrders/
    ├── PurchaseOrderReportServiceTests.cs        # (nuevo)
    ├── PurchaseOrderPdfDocumentTests.cs           # (nuevo)
    └── PurchaseOrderEmailServiceTests.cs           # (nuevo)
```

No se agrega ningún método nuevo a `IEpicorClient` en este plan — a diferencia del Plan 3, ningún `StubEpicorClient`/`TestEpicorClient` existente necesita tocarse.

---

## Task 1: Paquetes NuGet, configuración y licencia de QuestPDF

**Files:**
- Modify: `src/OCAutomatica.Api/OCAutomatica.Api.csproj`
- Modify: `src/OCAutomatica.Api/appsettings.json`
- Modify: `src/OCAutomatica.Api/Program.cs`
- Create: `src/OCAutomatica.Api/PurchaseOrders/EmailQueueOptions.cs`

**Por qué existe esta tarea:** las tareas siguientes (3, 4, 6) dependen de que estos paquetes ya estén instalados y de que exista la sección de configuración `EmailQueue` — sin esto no compilan. Cada tarea posterior registra su propio servicio nuevo en `Program.cs` cuando lo crea (ver sus propios steps) — así la solución compila, y todos los tests corren, después de cada tarea, no solo al final del plan.

- [ ] **Step 1: Instalar los paquetes**

```bash
cd src/OCAutomatica.Api
dotnet add package QuestPDF
dotnet add package Microsoft.Data.SqlClient
```

- [ ] **Step 2: Verificar que el proyecto sigue compilando**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 3: Agregar la sección `EmailQueue` a `appsettings.json`**

En `src/OCAutomatica.Api/appsettings.json`, agregar la nueva sección junto a `Epicor` (vacía — la cadena real va en `appsettings.Development.json`, que ya está en `.gitignore`):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Epicor": {
    "BaseUrl": "",
    "ApiKey": "",
    "AnchorCompany": ""
  },
  "EmailQueue": {
    "ConnectionString": ""
  }
}
```

- [ ] **Step 4: Agregar la cadena real a `appsettings.Development.json` (local, no versionado)**

En `src/OCAutomatica.Api/appsettings.Development.json`, agregar (junto a la sección `Epicor` que ya tiene el `BaseUrl`/`ApiKey` reales):

```json
{
  "EmailQueue": {
    "ConnectionString": "Server=192.168.100.18;Database=CFSJService;User Id=sa;Password=Epicor123;TrustServerCertificate=True;"
  }
}
```

(`TrustServerCertificate=True` porque este servidor no expone un certificado TLS confiable para el driver — mismo tipo de ajuste que cualquier conexión SQL a un servidor interno sin PKI corporativo; si el servidor sí lo tiene, se puede quitar.)

- [ ] **Step 5: Crear `EmailQueueOptions` (la clase, no solo la sección de config)**

`Program.cs` (Step 6) necesita `Configure<EmailQueueOptions>(...)`, y esa clase no existe todavía en el proyecto — créala ahora, en la ubicación y namespace que el resto del plan espera (Task 3 la consume tal cual, sin volver a crearla):

Crear `src/OCAutomatica.Api/PurchaseOrders/EmailQueueOptions.cs`:

```csharp
namespace OCAutomatica.Api.PurchaseOrders;

public sealed class EmailQueueOptions
{
    public const string SectionName = "EmailQueue";

    /// <summary>
    /// Connection string to the CFSJService database on 192.168.100.18 —
    /// the same server sp_EnviaOC_V2 inserts into via linked server. This
    /// app connects to it directly and never touches the Epicor SQL Server
    /// (SRVCSJPR2) or the stored procedure itself (see spec section 2.4).
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
```

- [ ] **Step 6: Registrar la licencia de QuestPDF y la configuración en `Program.cs`**

En `src/OCAutomatica.Api/Program.cs`, agregar las dos líneas nuevas (licencia + `Configure<EmailQueueOptions>`) donde ya está `Configure<EpicorOptions>`. **No agregues `using OCAutomatica.Api.Users;` todavía** — ese namespace no existe hasta la Task 2, y un `using` a un namespace inexistente no compila:

```csharp
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Buyers;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Organization;
using OCAutomatica.Api.Parts;
using OCAutomatica.Api.PurchaseOrders;
using OCAutomatica.Api.Vendors;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// QuestPDF Community license — free for organizations under $1M USD annual
// revenue (confirmed applicable, see spec section 8). Must be set once
// before any PurchaseOrderPdfDocument.GeneratePdf() call, or QuestPDF throws.
QuestPDF.Settings.License = LicenseType.Community;

builder.Services.AddControllers();
builder.Services.AddDataProtection();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<EpicorOptions>(
    builder.Configuration.GetSection(EpicorOptions.SectionName));
builder.Services.Configure<EmailQueueOptions>(
    builder.Configuration.GetSection(EmailQueueOptions.SectionName));

builder.Services.AddHttpClient<IEpicorClient, EpicorClient>();

builder.Services.AddSingleton<ISessionStore, SessionStore>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBuyerService, BuyerService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<ICambiosFisicosService, CambiosFisicosService>();
builder.Services.AddScoped<IPartService, PartService>();
builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
builder.Services.AddScoped<IPurchaseOrderHistoryService, PurchaseOrderHistoryService>();
builder.Services.AddScoped<IVendorService, VendorService>();
// IUserDirectoryService, IEmailQueueRepository, IPurchaseOrderReportService,
// IPurchaseOrderEmailService: registered by Tasks 2, 3, 4, and 7
// respectively, each when it creates its own type below.

var app = builder.Build();
```

**Nota sobre por qué la línea `Configure<EmailQueueOptions>` sí compila ya en este paso, pero ningún `AddScoped` de los cuatro servicios nuevos se agrega todavía:** el proyecto de tests referencia el ensamblado completo de la API — si `Program.cs` registrara un tipo que todavía no existe (como pasaría si esta Task agregara aquí los cuatro `AddScoped` de una vez), la solución completa deja de compilar y **ningún test de ningún archivo puede correr** hasta que exista el último tipo faltante, rompiendo el ciclo TDD de las Tasks 2-6. Por eso cada Task registra su propio servicio al crearlo (ver el Step correspondiente en las Tasks 2-4 y 7) — así `Program.cs` compila después de cada Task, no solo al final.

- [ ] **Step 7: Verificar que la solución completa compila y los tests existentes pasan**

Run: `dotnet test`
Expected: todos los tests existentes siguen pasando (esta Task no le quita cobertura a nada — solo agrega configuración nueva que ningún test todavía ejercita).

- [ ] **Step 8: Commit**

```bash
git add src/OCAutomatica.Api/OCAutomatica.Api.csproj src/OCAutomatica.Api/appsettings.json src/OCAutomatica.Api/Program.cs src/OCAutomatica.Api/PurchaseOrders/EmailQueueOptions.cs
git commit -m "chore: add QuestPDF and Microsoft.Data.SqlClient, configure EmailQueue options"
```

---

## Task 2: `IUserDirectoryService` — correo del usuario logueado

**Files:**
- Create: `src/OCAutomatica.Api/Users/IUserDirectoryService.cs`
- Create: `src/OCAutomatica.Api/Users/UserDirectoryService.cs`
- Test: `tests/OCAutomatica.Api.Tests/Users/UserDirectoryServiceTests.cs`

**Por qué existe esta tarea:** reemplaza los dos `IF/ELSE` fijos por planta del `.rpt` (sección 2.2 y 2.3 del spec) con el dato real y dinámico: el correo capturado en Epicor para el usuario que tiene la sesión abierta (`Ice.UserFile`, campo `Email` visible en *User Account Maintenance* — confirmado en vivo para el usuario `epicor`). Lo usan tanto el PDF (Task 4, pie de página) como el envío de copia (Task 7).

**Interfaces:**
- Consumes: `IEpicorClient.GetAsync<T>`, `EpicorOptions.AnchorCompany` (mismo patrón que `AuthService` — `Ice.UserFile` es una tabla system-wide, no depende de la compañía activa).
- Produces: `IUserDirectoryService.GetEmailAsync(string userId, EpicorCredentials credentials, CancellationToken ct = default)` → `Task<string?>` (`null` si el usuario no existe o no tiene correo capturado).

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/OCAutomatica.Api.Tests/Users/UserDirectoryServiceTests.cs`:

```csharp
using Microsoft.Extensions.Options;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Users;

namespace OCAutomatica.Api.Tests.Users;

public class UserDirectoryServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly Func<string, object?> _byPath;
        public List<string> RequestedPaths { get; } = new();

        public StubEpicorClient(Func<string, object?> byPath) => _byPath = byPath;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
        {
            RequestedPaths.Add(relativePath);
            return Task.FromResult((T?)_byPath(relativePath));
        }

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by UserDirectoryService.");

        public Task<T?> InvokeFunctionAsync<T>(string company, string library, string function,
            object input, EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by UserDirectoryService.");
    }

    private static readonly EpicorCredentials Creds = new("epicor", "x");
    private static readonly IOptions<EpicorOptions> Options =
        Microsoft.Extensions.Options.Options.Create(new EpicorOptions { AnchorCompany = "CFSJ_LAF" });

    [Fact]
    public async Task GetEmailAsync_ReturnsTheCapturedEmail()
    {
        var client = new StubEpicorClient(path =>
        {
            Assert.Contains("UserID eq 'epicor'", path);
            return new UserEmailListResponse
            {
                Value = new List<UserEmailDto> { new() { EMailAddress = "giovanni.montoya@carnessanjuan.com" } }
            };
        });
        var service = new UserDirectoryService(client, Options);

        var email = await service.GetEmailAsync("epicor", Creds);

        Assert.Equal("giovanni.montoya@carnessanjuan.com", email);
    }

    [Fact]
    public async Task GetEmailAsync_ReturnsNull_WhenUserHasNoEmailCaptured()
    {
        var client = new StubEpicorClient(_ => new UserEmailListResponse
        {
            Value = new List<UserEmailDto> { new() { EMailAddress = "" } }
        });
        var service = new UserDirectoryService(client, Options);

        var email = await service.GetEmailAsync("sinCorreo", Creds);

        Assert.Null(email);
    }

    [Fact]
    public async Task GetEmailAsync_ReturnsNull_WhenUserNotFound()
    {
        var client = new StubEpicorClient(_ => new UserEmailListResponse());
        var service = new UserDirectoryService(client, Options);

        var email = await service.GetEmailAsync("noExiste", Creds);

        Assert.Null(email);
    }

    [Fact]
    public async Task GetEmailAsync_QueriesAnchoredAtAnchorCompany_NotTheActiveCompany()
    {
        // Ice.UserFile is system-wide, same as Ice.UserComp in AuthService —
        // the company segment in the URL is required by Epicor's REST
        // surface but irrelevant to which rows come back.
        string? companyUsed = null;
        var client = new CapturingStubEpicorClient((company, path) =>
        {
            companyUsed = company;
            return new UserEmailListResponse
            {
                Value = new List<UserEmailDto> { new() { EMailAddress = "x@y.com" } }
            };
        });
        var service = new UserDirectoryService(client, Options);

        await service.GetEmailAsync("epicor", Creds);

        Assert.Equal("CFSJ_LAF", companyUsed);
    }

    private sealed class CapturingStubEpicorClient : IEpicorClient
    {
        private readonly Func<string, string, object?> _byCompanyAndPath;
        public CapturingStubEpicorClient(Func<string, string, object?> byCompanyAndPath) => _byCompanyAndPath = byCompanyAndPath;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_byCompanyAndPath(company, relativePath));

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<T?> InvokeFunctionAsync<T>(string company, string library, string function,
            object input, EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter UserDirectoryServiceTests`
Expected: FAIL — no existe `OCAutomatica.Api.Users`, ni `IUserDirectoryService`, ni `UserEmailListResponse`/`UserEmailDto`.

- [ ] **Step 3: Crear la interfaz**

Crear `src/OCAutomatica.Api/Users/IUserDirectoryService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Users;

public interface IUserDirectoryService
{
    Task<string?> GetEmailAsync(
        string userId,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Implementar el servicio**

Crear `src/OCAutomatica.Api/Users/UserDirectoryService.cs`:

```csharp
using Microsoft.Extensions.Options;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Users;

public sealed class UserDirectoryService : IUserDirectoryService
{
    // Ice.UserFile is system-wide (same as Ice.UserComp in AuthService) —
    // the company segment in the URL is required by Epicor's REST surface
    // but irrelevant to which user record comes back.
    private const string UserEmailPathTemplate =
        "Ice.BO.UserFileSvc/UserFiles?$filter=UserID eq '{0}'&$select=EMailAddress&$top=1";

    private readonly IEpicorClient _epicor;
    private readonly EpicorOptions _options;

    public UserDirectoryService(IEpicorClient epicor, IOptions<EpicorOptions> options)
    {
        _epicor = epicor;
        _options = options.Value;
    }

    public async Task<string?> GetEmailAsync(
        string userId,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var escaped = Uri.EscapeDataString(userId.Replace("'", "''"));
        var path = string.Format(UserEmailPathTemplate, escaped);

        var response = await _epicor.GetAsync<UserEmailListResponse>(
            _options.AnchorCompany, path, credentials, ct);

        var email = response?.Value.FirstOrDefault()?.EMailAddress;
        return string.IsNullOrWhiteSpace(email) ? null : email;
    }
}

public sealed class UserEmailListResponse
{
    public List<UserEmailDto> Value { get; set; } = new();
}

public sealed class UserEmailDto
{
    public string EMailAddress { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Correr los tests para verificar que pasan**

Run: `dotnet test --filter UserDirectoryServiceTests`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 6: Verificar en vivo contra Epicor real (antes de seguir a la Task 4)**

Con Swagger (o el ambiente real), confirmar que `Ice.BO.UserFileSvc/UserFiles?$filter=UserID eq 'epicor'&$select=EMailAddress` devuelve `giovanni.montoya@carnessanjuan.com` (el correo visto en *User Account Maintenance*). Si el campo real se llama distinto (por ejemplo `Email` en vez de `EMailAddress`), ajustar `UserEmailDto`/el `$select` antes de continuar — el resto del plan depende de que este campo funcione.

- [ ] **Step 7: Commit**

```bash
git add src/OCAutomatica.Api/Users tests/OCAutomatica.Api.Tests/Users
git commit -m "feat: add IUserDirectoryService to resolve the logged-in user's email from Epicor"
```

---

## Task 3: `IEmailQueueRepository` — insert tipado a `interfaz_envia_oc`

**Files:**
- Create: `src/OCAutomatica.Api/PurchaseOrders/IEmailQueueRepository.cs`
- Create: `src/OCAutomatica.Api/PurchaseOrders/EmailQueueRepository.cs`
- Modify: `src/OCAutomatica.Api/Program.cs`

**Por qué existe esta tarea:** es el reemplazo del `EXECUTE sp_EnviaOC_V2 '{0}', '{1}', ...` armado con `String.Format` (defecto 10.8 del spec original) — inserta directo en `192.168.100.18` con `SqlParameter` tipados, sin tocar el SQL Server de Epicor.

**Nota sobre pruebas:** al igual que `EpicorClient` (que se prueba con un `HttpMessageHandler` falso porque `HttpClient` es fakeable así), `Microsoft.Data.SqlClient.SqlConnection` **no** tiene una interfaz nativa fakeable sin una capa de abstracción propia. Este proyecto no agrega esa capa extra solo para poder mockear el driver — en su lugar, `EmailQueueRepository` se prueba **manualmente contra la base real** (Step 4), y los servicios que lo consumen (Task 7) se prueban con un **fake de `IEmailQueueRepository`** — exactamente el mismo patrón que ya usa este proyecto para `IEpicorClient` en los servicios que lo consumen.

**Interfaces:**
- Consumes: `EmailQueueOptions.ConnectionString` (vía `IOptions<EmailQueueOptions>`) — la clase ya existe, creada en la Task 1 (`src/OCAutomatica.Api/PurchaseOrders/EmailQueueOptions.cs`). No la vuelvas a crear.
- Produces: `IEmailQueueRepository.InsertAsync(EmailQueueEntry entry, CancellationToken ct = default)` → `Task`. `EmailQueueEntry(string Company, string CompanyName, string Plant, string PlantName, string PoNumber, string VendorId, string VendorName, string Emails, int OcTipo)`.

- [ ] **Step 1: Definir la interfaz y el registro a insertar**

Crear `src/OCAutomatica.Api/PurchaseOrders/IEmailQueueRepository.cs`:

```csharp
namespace OCAutomatica.Api.PurchaseOrders;

/// <summary>
/// One row to insert into interfaz_envia_oc — same columns sp_EnviaOC_V2
/// fills, minus fechaInserto/fechaUltimaRevision/fechaProceso/estatus/mensaje,
/// which the repository sets to their fixed initial values itself.
/// </summary>
public sealed record EmailQueueEntry(
    string Company,
    string CompanyName,
    string Plant,
    string PlantName,
    string PoNumber,
    string VendorId,
    string VendorName,
    string Emails,
    int OcTipo);

public interface IEmailQueueRepository
{
    Task InsertAsync(EmailQueueEntry entry, CancellationToken ct = default);
}
```

- [ ] **Step 2: Implementar el repositorio**

Crear `src/OCAutomatica.Api/PurchaseOrders/EmailQueueRepository.cs`:

```csharp
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace OCAutomatica.Api.PurchaseOrders;

public sealed class EmailQueueRepository : IEmailQueueRepository
{
    // Column sizes match sp_EnviaOC_V2's own parameter declarations exactly
    // (confirmed against the real stored procedure — see spec section 2.4).
    private const string InsertSql = """
        INSERT INTO dbo.interfaz_envia_oc
            (company, companyName, plant, plantName, poNumber, vendorID, vendorName,
             emails, fechaInserto, fechaUltimaRevision, fechaProceso, estatus, mensaje, oc_tipo)
        VALUES
            (@company, @companyName, @plant, @plantName, @poNumber, @vendorID, @vendorName,
             @emails, GETDATE(), NULL, NULL, 0, '', @oc_tipo)
        """;

    private readonly string _connectionString;

    public EmailQueueRepository(IOptions<EmailQueueOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task InsertAsync(EmailQueueEntry entry, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand(InsertSql, connection);
        command.Parameters.Add(new SqlParameter("@company", SqlDbType.VarChar, 20) { Value = entry.Company });
        command.Parameters.Add(new SqlParameter("@companyName", SqlDbType.VarChar, 500) { Value = entry.CompanyName });
        command.Parameters.Add(new SqlParameter("@plant", SqlDbType.VarChar, 20) { Value = entry.Plant });
        command.Parameters.Add(new SqlParameter("@plantName", SqlDbType.VarChar, 500) { Value = entry.PlantName });
        command.Parameters.Add(new SqlParameter("@poNumber", SqlDbType.VarChar, 50) { Value = entry.PoNumber });
        command.Parameters.Add(new SqlParameter("@vendorID", SqlDbType.VarChar, 50) { Value = entry.VendorId });
        command.Parameters.Add(new SqlParameter("@vendorName", SqlDbType.VarChar, 500) { Value = entry.VendorName });
        command.Parameters.Add(new SqlParameter("@emails", SqlDbType.VarChar, -1) { Value = entry.Emails }); // -1 = varchar(max)
        command.Parameters.Add(new SqlParameter("@oc_tipo", SqlDbType.Int) { Value = entry.OcTipo });

        await command.ExecuteNonQueryAsync(ct);
    }
}
```

- [ ] **Step 3: Registrar el servicio en `Program.cs`**

En `src/OCAutomatica.Api/Program.cs`, reemplazar la línea `builder.Services.AddScoped<IVendorService, VendorService>();` y el comentario que la sigue por:

```csharp
builder.Services.AddScoped<IVendorService, VendorService>();
builder.Services.AddScoped<IEmailQueueRepository, EmailQueueRepository>();
// IPurchaseOrderReportService, IPurchaseOrderEmailService: registered by
// Tasks 4 and 7 respectively, each when it creates its own type.
```

(`using OCAutomatica.Api.PurchaseOrders;` ya está en `Program.cs` desde antes de este plan — no hace falta agregarlo.)

- [ ] **Step 4: Verificar que la solución completa compila y los tests existentes pasan**

Run: `dotnet test`
Expected: todos los tests existentes siguen pasando — esta Task no agrega tests nuevos (ver la nota sobre pruebas arriba), solo código de producción.

- [ ] **Step 5: Verificación manual real (una sola vez, contra la base real)**

Con la cadena de conexión real ya en `appsettings.Development.json` (Task 1), escribir un pequeño programa de una sola vez (o usar `dotnet-script`/SSMS) que llame `EmailQueueRepository.InsertAsync` con un `EmailQueueEntry` de prueba (`OcTipo = 99`, un valor que no se use en producción, para poder identificarlo y borrarlo después) y confirmar en SSMS que la fila aparece en `192.168.100.18` con `estatus = 0` y las columnas correctas. Borrar la fila de prueba al terminar. No hace falta dejar este programa de prueba en el repositorio.

- [ ] **Step 6: Commit**

```bash
git add src/OCAutomatica.Api/PurchaseOrders/IEmailQueueRepository.cs src/OCAutomatica.Api/PurchaseOrders/EmailQueueRepository.cs src/OCAutomatica.Api/Program.cs
git commit -m "feat: add IEmailQueueRepository for typed inserts into interfaz_envia_oc"
```

---

## Task 4: Modelos y `PurchaseOrderReportService` — recolección de datos del PDF

**Files:**
- Create: `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderReportModels.cs`
- Create: `src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderReportService.cs`
- Create: `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderReportService.cs`
- Modify: `src/OCAutomatica.Api/Program.cs`
- Test: `tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderReportServiceTests.cs`

**Por qué existe esta tarea:** junta, con llamadas OData, exactamente los mismos datos que traía el query embebido del `.rpt` de Crystal (spec, sección 2.1) — encabezado, líneas, dirección del proveedor, dirección de entrega con su fallback a la compañía, nombre/teléfono de planta, descripción del ShipVia, código EAN por línea, y el correo del comprador logueado (vía `IUserDirectoryService`, Task 2).

**Interfaces:**
- Consumes: `IEpicorClient.GetAsync<T>`, `IUserDirectoryService.GetEmailAsync`, `TimeProvider.GetUtcNow()`, `PODetailDto` (ya existente en `PurchaseOrderHistoryModels.cs`).
- Produces: `IPurchaseOrderReportService.GetReportDataAsync(string company, string plant, int poNum, string username, EpicorCredentials credentials, CancellationToken ct = default)` → `Task<PurchaseOrderReportData?>` (`null` si la OC no existe). `PurchaseOrderReportData` y `PurchaseOrderReportLine` — ver Step 3, se usan en la Task 5 (`PurchaseOrderPdfDocument`) y en la Task 6 (controlador).

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderReportServiceTests.cs`:

```csharp
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.PurchaseOrders;
using OCAutomatica.Api.Users;

namespace OCAutomatica.Api.Tests.PurchaseOrders;

public class PurchaseOrderReportServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly Func<string, object?> _byPath;
        public List<string> RequestedPaths { get; } = new();

        public StubEpicorClient(Func<string, object?> byPath) => _byPath = byPath;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
        {
            RequestedPaths.Add(relativePath);
            return Task.FromResult((T?)_byPath(relativePath));
        }

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by PurchaseOrderReportService.");

        public Task<T?> InvokeFunctionAsync<T>(string company, string library, string function,
            object input, EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by PurchaseOrderReportService.");
    }

    private sealed class StubUserDirectoryService : IUserDirectoryService
    {
        private readonly string? _email;
        public StubUserDirectoryService(string? email) => _email = email;
        public Task<string?> GetEmailAsync(string userId, EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult(_email);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static readonly EpicorCredentials Creds = new("epicor", "x");
    private static readonly TimeProvider July2026 =
        new FixedTimeProvider(new DateTimeOffset(2026, 7, 23, 13, 39, 34, TimeSpan.Zero));

    private static PurchaseOrderReportHeaderDto BuildHeader() => new()
    {
        PONum = 3431,
        Approve = true,
        ApprovalStatus = "A",
        BuyerIDName = "Janneth Yañez",
        VendorVendorID = "001008",
        VendorName = "JARAMILLO TREVIÑO GERARDO MAGDALENO",
        OrderDate = new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero),
        ShipViaCode = "LOC",
        CommentText = "CALABAZA GRANDE",
        ShipAddress1 = "",
        ShipAddress2 = "",
        ShipCity = "",
        ShipState = "",
        ShipZIP = "",
        DocTotalTax = 0m,
        TotalWhTax = 0m,
        PODetails = new List<PODetailDto>
        {
            new() { PONUM = 3431, POLine = 1, PartNum = "8323700247", LineDesc = "CALABACITA KG", OrderQty = 4m, IUM = "KGS", UnitCost = 14m },
        }
    };

    private static IEpicorClient BuildClientForFullHappyPath()
    {
        return new StubEpicorClient(path =>
        {
            if (path.Contains("POes")) return BuildHeader();
            if (path.Contains("VendorSvc"))
                return new VendorAddressListResponse
                {
                    Value = new List<VendorAddressDto> { new() { Address1 = "AV LOS ANGELES", Address2 = "1000", City = "SAN NICOLAS DE LOS GARZA" } }
                };
            if (path.Contains("CompanySvc"))
                return new CompanyAddressListResponse
                {
                    Value = new List<CompanyAddressDto>
                    {
                        new() { Address1 = "AVE. ACAPULCO NUM. 1100 Col. RESIDENCIAL SANTA FE", Address2 = "", City = "GUADALUPE", State = "NUEVO LEON", Zip = "67112", StateTaxID = "CFS051213DG5" }
                    }
                };
            if (path.Contains("PlantSvc"))
                return new PlantDetailsListResponse
                {
                    Value = new List<PlantDetailsDto> { new() { Name = "LA FE", PhoneNum = "" } }
                };
            if (path.Contains("ShipViaSvc"))
                return new ShipViaListResponse
                {
                    Value = new List<ShipViaDto> { new() { Description = "LOCAL" } }
                };
            if (path.Contains("PartPCs"))
                return new PartEanListResponse
                {
                    Value = new List<PartEanDto> { new() { PartNum = "8323700247", UOMCode = "KGS", PRODCODE = "12" } }
                };
            throw new InvalidOperationException($"Unexpected path: {path}");
        });
    }

    [Fact]
    public async Task GetReportDataAsync_BuildsTheFullReportFromAllSources()
    {
        var service = new PurchaseOrderReportService(
            BuildClientForFullHappyPath(), new StubUserDirectoryService("giovanni.montoya@carnessanjuan.com"), July2026);

        var data = await service.GetReportDataAsync("CFSJ_LAF", "LAF", 3431, "epicor", Creds);

        Assert.NotNull(data);
        Assert.Equal(3431, data!.PoNum);
        Assert.True(data.Approved);
        Assert.Equal("LA FE", data.PlantName);
        Assert.Equal("001008", data.VendorId);
        Assert.Equal("AV LOS ANGELES", data.VendorAddress1);
        Assert.Equal("LOCAL", data.ShipViaDescription);
        Assert.Equal("Janneth Yañez", data.BuyerName);
        Assert.Equal("CALABAZA GRANDE", data.CommentText);
        Assert.Equal("giovanni.montoya@carnessanjuan.com", data.BuyerEmail);

        Assert.Single(data.Lines);
        var line = data.Lines[0];
        Assert.Equal(1, line.Line);
        Assert.Equal("8323700247", line.PartNum);
        Assert.Equal("12", line.Ean);
        Assert.Equal(4m, line.OrderQty);
        Assert.Equal(14m, line.UnitCost);
        Assert.Equal(56m, line.ExtendedPrice); // 4 * 14

        Assert.Equal(56m, data.Subtotal);
        Assert.Equal(0m, data.Taxes);
        Assert.Equal(0m, data.Withholdings);
        Assert.Equal(56m, data.Total);
    }

    [Fact]
    public async Task GetReportDataAsync_UsesShipAddress_WhenCaptured()
    {
        // Matches the legacy CASE formula: only falls back to Company's
        // address when POHeader.ShipAddress1 is blank (spec section 2.1).
        var client = new StubEpicorClient(path =>
        {
            if (path.Contains("POes"))
            {
                var header = BuildHeader();
                header.ShipAddress1 = "CALLE FALSA 123";
                header.ShipCity = "MONTERREY";
                header.ShipState = "NUEVO LEON";
                header.ShipZIP = "64000";
                return header;
            }
            if (path.Contains("VendorSvc")) return new VendorAddressListResponse();
            if (path.Contains("CompanySvc")) return new CompanyAddressListResponse();
            if (path.Contains("PlantSvc"))
                return new PlantDetailsListResponse { Value = new List<PlantDetailsDto> { new() { Name = "LA FE", PhoneNum = "8112345678" } } };
            if (path.Contains("ShipViaSvc")) return new ShipViaListResponse();
            if (path.Contains("PartPCs")) return new PartEanListResponse();
            throw new InvalidOperationException($"Unexpected path: {path}");
        });
        var service = new PurchaseOrderReportService(client, new StubUserDirectoryService(null), July2026);

        var data = await service.GetReportDataAsync("CFSJ_LAF", "LAF", 3431, "epicor", Creds);

        Assert.Contains("CALLE FALSA 123", data!.DeliveryAddress);
        Assert.Contains("MONTERREY", data.DeliveryAddress);
        Assert.Contains("8112345678", data.DeliveryAddress);
        Assert.DoesNotContain("AVE. ACAPULCO", data.DeliveryAddress);
    }

    [Fact]
    public async Task GetReportDataAsync_FallsBackToCompanyAddress_WhenShipAddressIsBlank()
    {
        var client = BuildClientForFullHappyPath();
        var service = new PurchaseOrderReportService(client, new StubUserDirectoryService(null), July2026);

        var data = await service.GetReportDataAsync("CFSJ_LAF", "LAF", 3431, "epicor", Creds);

        Assert.Contains("AVE. ACAPULCO NUM. 1100", data!.DeliveryAddress);
        Assert.Contains("GUADALUPE", data.DeliveryAddress);
        Assert.Contains("CFS051213DG5", data.DeliveryAddress);
    }

    [Fact]
    public async Task GetReportDataAsync_ReturnsNull_WhenPurchaseOrderNotFound()
    {
        var client = new StubEpicorClient(_ => new PurchaseOrderReportHeaderDto { PONum = 0 });
        var service = new PurchaseOrderReportService(client, new StubUserDirectoryService(null), July2026);

        var data = await service.GetReportDataAsync("CFSJ_LAF", "LAF", 999999, "epicor", Creds);

        Assert.Null(data);
    }

    [Fact]
    public async Task GetReportDataAsync_LeavesEanBlank_WhenPartHasNoEanCaptured()
    {
        var client = new StubEpicorClient(path =>
        {
            if (path.Contains("POes")) return BuildHeader();
            if (path.Contains("PartPCs")) return new PartEanListResponse(); // no matches
            if (path.Contains("VendorSvc")) return new VendorAddressListResponse();
            if (path.Contains("CompanySvc")) return new CompanyAddressListResponse();
            if (path.Contains("PlantSvc")) return new PlantDetailsListResponse();
            if (path.Contains("ShipViaSvc")) return new ShipViaListResponse();
            throw new InvalidOperationException($"Unexpected path: {path}");
        });
        var service = new PurchaseOrderReportService(client, new StubUserDirectoryService(null), July2026);

        var data = await service.GetReportDataAsync("CFSJ_LAF", "LAF", 3431, "epicor", Creds);

        Assert.Equal(string.Empty, data!.Lines[0].Ean);
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter PurchaseOrderReportServiceTests`
Expected: FAIL — ninguno de los tipos nuevos existe todavía.

- [ ] **Step 3: Crear los modelos**

Crear `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderReportModels.cs`:

```csharp
namespace OCAutomatica.Api.PurchaseOrders;

public sealed record PurchaseOrderReportData(
    int PoNum,
    string PlantName,
    bool Approved,
    DateTimeOffset? OrderDate,
    DateTimeOffset PrintedAt,
    string VendorId,
    string VendorName,
    string VendorAddress1,
    string VendorAddress2,
    string VendorCity,
    string DeliveryAddress,
    string ShipViaDescription,
    string BuyerName,
    string CommentText,
    string BuyerEmail,
    IReadOnlyList<PurchaseOrderReportLine> Lines,
    decimal Subtotal,
    decimal Taxes,
    decimal Withholdings,
    decimal Total);

public sealed record PurchaseOrderReportLine(
    int Line,
    string PartNum,
    string Description,
    string Ean,
    decimal OrderQty,
    string Uom,
    decimal UnitCost,
    decimal ExtendedPrice);

// --- Raw Epicor response shapes ---

public sealed class PurchaseOrderReportHeaderDto
{
    public int PONum { get; set; }
    public bool Approve { get; set; }
    public string ApprovalStatus { get; set; } = string.Empty;
    public string BuyerIDName { get; set; } = string.Empty;
    public string VendorVendorID { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public DateTimeOffset? OrderDate { get; set; }
    public string ShipViaCode { get; set; } = string.Empty;
    public string CommentText { get; set; } = string.Empty;
    public string ShipAddress1 { get; set; } = string.Empty;
    public string ShipAddress2 { get; set; } = string.Empty;
    public string ShipCity { get; set; } = string.Empty;
    public string ShipState { get; set; } = string.Empty;
    public string ShipZIP { get; set; } = string.Empty;
    public decimal DocTotalTax { get; set; }
    public decimal TotalWhTax { get; set; }
    public List<PODetailDto> PODetails { get; set; } = new();
}

public sealed class VendorAddressListResponse
{
    public List<VendorAddressDto> Value { get; set; } = new();
}

public sealed class VendorAddressDto
{
    public string Address1 { get; set; } = string.Empty;
    public string Address2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}

public sealed class CompanyAddressListResponse
{
    public List<CompanyAddressDto> Value { get; set; } = new();
}

public sealed class CompanyAddressDto
{
    public string Address1 { get; set; } = string.Empty;
    public string Address2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Zip { get; set; } = string.Empty;
    public string StateTaxID { get; set; } = string.Empty;
}

public sealed class PlantDetailsListResponse
{
    public List<PlantDetailsDto> Value { get; set; } = new();
}

public sealed class PlantDetailsDto
{
    public string Name { get; set; } = string.Empty;
    public string PhoneNum { get; set; } = string.Empty;
}

public sealed class ShipViaListResponse
{
    public List<ShipViaDto> Value { get; set; } = new();
}

public sealed class ShipViaDto
{
    public string Description { get; set; } = string.Empty;
}

public sealed class PartEanListResponse
{
    public List<PartEanDto> Value { get; set; } = new();
}

public sealed class PartEanDto
{
    public string PartNum { get; set; } = string.Empty;
    public string UOMCode { get; set; } = string.Empty;
    public string PRODCODE { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Crear la interfaz**

Crear `src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderReportService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.PurchaseOrders;

public interface IPurchaseOrderReportService
{
    Task<PurchaseOrderReportData?> GetReportDataAsync(
        string company,
        string plant,
        int poNum,
        string username,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
```

- [ ] **Step 5: Implementar el servicio**

Crear `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderReportService.cs`:

```csharp
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Users;

namespace OCAutomatica.Api.PurchaseOrders;

public sealed class PurchaseOrderReportService : IPurchaseOrderReportService
{
    private readonly IEpicorClient _epicor;
    private readonly IUserDirectoryService _users;
    private readonly TimeProvider _time;

    public PurchaseOrderReportService(IEpicorClient epicor, IUserDirectoryService users, TimeProvider time)
    {
        _epicor = epicor;
        _users = users;
        _time = time;
    }

    public async Task<PurchaseOrderReportData?> GetReportDataAsync(
        string company,
        string plant,
        int poNum,
        string username,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var headerPath = $"Erp.BO.POSvc/POes('{company}',{poNum})?$expand=PODetails";
        var header = await _epicor.GetAsync<PurchaseOrderReportHeaderDto>(company, headerPath, credentials, ct);
        if (header is null || header.PONum == 0) return null;

        var vendorTask = GetVendorAddressAsync(company, header.VendorVendorID, credentials, ct);
        var companyTask = GetCompanyAddressAsync(company, credentials, ct);
        var plantTask = GetPlantDetailsAsync(company, plant, credentials, ct);
        var shipViaTask = string.IsNullOrWhiteSpace(header.ShipViaCode)
            ? Task.FromResult<ShipViaDto?>(null)
            : GetShipViaAsync(company, header.ShipViaCode, credentials, ct);
        var eanTask = GetEanCodesAsync(company, header.PODetails, credentials, ct);
        var emailTask = _users.GetEmailAsync(username, credentials, ct);

        await Task.WhenAll(vendorTask, companyTask, plantTask, shipViaTask, eanTask, emailTask);

        var vendor = vendorTask.Result;
        var companyAddr = companyTask.Result;
        var plantDetails = plantTask.Result;
        var shipVia = shipViaTask.Result;
        var eanByPart = eanTask.Result;

        var deliveryAddress = BuildDeliveryAddress(header, companyAddr, plantDetails?.PhoneNum ?? string.Empty);

        var lines = header.PODetails
            .OrderBy(d => d.POLine)
            .Select(d => new PurchaseOrderReportLine(
                d.POLine,
                d.PartNum,
                d.LineDesc,
                eanByPart.GetValueOrDefault((d.PartNum, d.IUM), string.Empty),
                d.OrderQty,
                d.IUM,
                d.UnitCost,
                d.OrderQty * d.UnitCost))
            .ToList();

        var subtotal = lines.Sum(l => l.ExtendedPrice);

        return new PurchaseOrderReportData(
            header.PONum,
            plantDetails?.Name ?? string.Empty,
            header.Approve,
            header.OrderDate,
            _time.GetUtcNow(),
            header.VendorVendorID,
            header.VendorName,
            vendor?.Address1 ?? string.Empty,
            vendor?.Address2 ?? string.Empty,
            vendor?.City ?? string.Empty,
            deliveryAddress,
            shipVia?.Description ?? string.Empty,
            header.BuyerIDName,
            header.CommentText,
            emailTask.Result ?? string.Empty,
            lines,
            subtotal,
            header.DocTotalTax,
            header.TotalWhTax,
            subtotal + header.DocTotalTax - header.TotalWhTax);
    }

    private async Task<VendorAddressDto?> GetVendorAddressAsync(
        string company, string vendorId, EpicorCredentials credentials, CancellationToken ct)
    {
        var escaped = Uri.EscapeDataString(vendorId.Replace("'", "''"));
        var path = $"Erp.BO.VendorSvc/Vendors?$filter=VendorID eq '{escaped}'&$select=Address1,Address2,City&$top=1";
        var response = await _epicor.GetAsync<VendorAddressListResponse>(company, path, credentials, ct);
        return response?.Value.FirstOrDefault();
    }

    private async Task<CompanyAddressDto?> GetCompanyAddressAsync(
        string company, EpicorCredentials credentials, CancellationToken ct)
    {
        var escaped = Uri.EscapeDataString(company.Replace("'", "''"));
        var path = $"Erp.BO.CompanySvc/Companies?$filter=Company eq '{escaped}'&$select=Address1,Address2,City,State,Zip,StateTaxID&$top=1";
        var response = await _epicor.GetAsync<CompanyAddressListResponse>(company, path, credentials, ct);
        return response?.Value.FirstOrDefault();
    }

    private async Task<PlantDetailsDto?> GetPlantDetailsAsync(
        string company, string plant, EpicorCredentials credentials, CancellationToken ct)
    {
        var escaped = Uri.EscapeDataString(plant.Replace("'", "''"));
        var path = $"Erp.BO.PlantSvc/Plants?$filter=Plant1 eq '{escaped}'&$select=Name,PhoneNum&$top=1";
        var response = await _epicor.GetAsync<PlantDetailsListResponse>(company, path, credentials, ct);
        return response?.Value.FirstOrDefault();
    }

    private async Task<ShipViaDto?> GetShipViaAsync(
        string company, string shipViaCode, EpicorCredentials credentials, CancellationToken ct)
    {
        var escaped = Uri.EscapeDataString(shipViaCode.Replace("'", "''"));
        var path = $"Erp.BO.ShipViaSvc/ShipVias?$filter=ShipViaCode eq '{escaped}'&$select=Description&$top=1";
        var response = await _epicor.GetAsync<ShipViaListResponse>(company, path, credentials, ct);
        return response?.Value.FirstOrDefault();
    }

    private async Task<Dictionary<(string PartNum, string Uom), string>> GetEanCodesAsync(
        string company, List<PODetailDto> lines, EpicorCredentials credentials, CancellationToken ct)
    {
        if (lines.Count == 0) return new();

        var pairs = lines
            .Select(l => (l.PartNum, l.IUM))
            .Distinct()
            .ToList();

        var filterClauses = pairs.Select(p =>
        {
            var part = Uri.EscapeDataString(p.PartNum.Replace("'", "''"));
            var uom = Uri.EscapeDataString(p.IUM.Replace("'", "''"));
            return $"(PartNum eq '{part}' and UOMCode eq '{uom}')";
        });

        var path = $"Erp.BO.PartSvc/PartPCs?$filter=PCType eq 'EAN-13' and ({string.Join(" or ", filterClauses)})" +
            "&$select=PartNum,UOMCode,PRODCODE";
        var response = await _epicor.GetAsync<PartEanListResponse>(company, path, credentials, ct);

        return (response?.Value ?? new List<PartEanDto>())
            .ToDictionary(e => (e.PartNum, e.UOMCode), e => e.PRODCODE);
    }

    private static string BuildDeliveryAddress(
        PurchaseOrderReportHeaderDto header, CompanyAddressDto? company, string plantPhone)
    {
        // Mirrors the legacy Crystal report's CASE formula exactly (spec
        // section 2.1): fall back to the Company's own address only when
        // the PO's ShipAddress fields are blank.
        var address1 = string.IsNullOrEmpty(header.ShipAddress1) ? company?.Address1 ?? string.Empty : header.ShipAddress1;
        var address2 = string.IsNullOrEmpty(header.ShipAddress2) ? company?.Address2 ?? string.Empty : header.ShipAddress2;
        var city = string.IsNullOrEmpty(header.ShipCity) ? company?.City ?? string.Empty : header.ShipCity;
        var state = string.IsNullOrEmpty(header.ShipState) ? company?.State ?? string.Empty : header.ShipState;
        var zip = string.IsNullOrEmpty(header.ShipZIP) ? company?.Zip ?? string.Empty : header.ShipZIP;

        return $"{address1}  {address2}  {city} {state} CP:{zip} TEL:{plantPhone} RFC: {company?.StateTaxID ?? string.Empty}";
    }
}
```

- [ ] **Step 6: Correr los tests para verificar que pasan**

Run: `dotnet test --filter PurchaseOrderReportServiceTests`
Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 7: Registrar el servicio en `Program.cs`**

En `src/OCAutomatica.Api/Program.cs`, reemplazar el comentario `// IPurchaseOrderReportService, IPurchaseOrderEmailService: registered by...` por:

```csharp
builder.Services.AddScoped<IPurchaseOrderReportService, PurchaseOrderReportService>();
// IPurchaseOrderEmailService: registered by Task 7, when it creates the type.
```

- [ ] **Step 8: Verificar que la solución completa compila y todos los tests pasan**

Run: `dotnet test`
Expected: todos los tests existentes siguen pasando, más los 5 nuevos de esta Task.

- [ ] **Step 9: Verificar en vivo contra Epicor real**

Antes de seguir a la Task 5, confirmar por Swagger que cada uno de estos campos existe con el nombre usado aquí: `Erp.BO.CompanySvc/Companies` (`Address1`, `Address2`, `City`, `State`, `Zip`, `StateTaxID`), `Erp.BO.VendorSvc/Vendors` (`Address1`, `Address2`, `City`), `Erp.BO.PlantSvc/Plants` (`PhoneNum` — `Name`/`Plant1` ya están confirmados por `OrganizationService`), `Erp.BO.ShipViaSvc/ShipVias` (`ShipViaCode`, `Description`), `Erp.BO.PartSvc/PartPCs` (`PartNum`, `UOMCode`, `PCType`, `PRODCODE`), y en el header de `POes` — `ShipAddress1/2`, `ShipCity`, `ShipState`, `ShipZIP`, `DocTotalTax`, `TotalWhTax`, `ShipViaCode`. Si algún nombre difiere, ajustar el DTO correspondiente (la deserialización insensible a mayúsculas ya cubre diferencias de casing, no de nombre).

- [ ] **Step 10: Commit**

```bash
git add src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderReportModels.cs src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderReportService.cs src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderReportService.cs src/OCAutomatica.Api/Program.cs tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderReportServiceTests.cs
git commit -m "feat: add PurchaseOrderReportService to gather all data for the PO PDF report"
```

---

## Task 5: `PurchaseOrderPdfDocument` — layout QuestPDF

**Files:**
- Create: `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderPdfDocument.cs`
- Test: `tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderPdfDocumentTests.cs`

**Por qué existe esta tarea:** convierte un `PurchaseOrderReportData` (Task 4) en el PDF final, replicando la estructura del reporte de ejemplo real (`ejmploOC.pdf`, OC 3431) confirmada con el usuario: bloque de encabezado + recuadros solo en la primera página, tabla de líneas que fluye entre páginas, totales/comentarios al final, y pie de página fijo en cada página (spec, sección 3).

**Interfaces:**
- Consumes: `PurchaseOrderReportData`, `PurchaseOrderReportLine` (Task 4).
- Produces: `PurchaseOrderPdfDocument(PurchaseOrderReportData data)` implementando `QuestPDF.Infrastructure.IDocument`, con el método de extensión `GeneratePdf()` (de `QuestPDF.Fluent`) disponible sobre cualquier instancia — usado por el controlador en la Task 6.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderPdfDocumentTests.cs`:

```csharp
using OCAutomatica.Api.PurchaseOrders;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace OCAutomatica.Api.Tests.PurchaseOrders;

public class PurchaseOrderPdfDocumentTests
{
    static PurchaseOrderPdfDocumentTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static PurchaseOrderReportData BuildSampleData(int lineCount = 1) => new(
        PoNum: 3431,
        PlantName: "LA FE",
        Approved: true,
        OrderDate: new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero),
        PrintedAt: new DateTimeOffset(2026, 7, 23, 13, 39, 34, TimeSpan.Zero),
        VendorId: "001008",
        VendorName: "JARAMILLO TREVIÑO GERARDO MAGDALENO",
        VendorAddress1: "AV LOS ANGELES",
        VendorAddress2: "1000",
        VendorCity: "SAN NICOLAS DE LOS GARZA",
        DeliveryAddress: "AVE. ACAPULCO NUM. 1100  Col. RESIDENCIAL SANTA FE  GUADALUPE NUEVO LEON CP:67112 TEL: RFC: CFS051213DG5",
        ShipViaDescription: "LOCAL",
        BuyerName: "Janneth Yañez",
        CommentText: "CALABAZA GRANDE",
        BuyerEmail: "giovanni.montoya@carnessanjuan.com",
        Lines: Enumerable.Range(1, lineCount)
            .Select(i => new PurchaseOrderReportLine(i, $"PART{i}", $"Descripcion {i}", "12", 4m, "KGS", 14m, 56m))
            .ToList(),
        Subtotal: 56m * lineCount,
        Taxes: 0m,
        Withholdings: 0m,
        Total: 56m * lineCount);

    [Fact]
    public void GeneratePdf_ProducesAValidPdf()
    {
        var document = new PurchaseOrderPdfDocument(BuildSampleData());

        var bytes = document.GeneratePdf();

        Assert.NotEmpty(bytes);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void GeneratePdf_HandlesManyLines_WithoutThrowing()
    {
        // Forces the table to flow across multiple pages — the scenario the
        // header/footer split (Task 5) exists to handle correctly.
        var document = new PurchaseOrderPdfDocument(BuildSampleData(lineCount: 60));

        var bytes = document.GeneratePdf();

        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void GeneratePdf_HandlesEmptyOptionalFields_WithoutThrowing()
    {
        var data = BuildSampleData() with
        {
            CommentText = string.Empty,
            ShipViaDescription = string.Empty,
            BuyerEmail = string.Empty,
        };
        var document = new PurchaseOrderPdfDocument(data);

        var bytes = document.GeneratePdf();

        Assert.NotEmpty(bytes);
    }
}
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `dotnet test --filter PurchaseOrderPdfDocumentTests`
Expected: FAIL — `PurchaseOrderPdfDocument` no existe.

- [ ] **Step 3: Implementar el documento**

**Nota sobre la API de QuestPDF usada abajo:** a diferencia del resto de este plan (nombres de campo confirmados en vivo contra Epicor/SSMS), la superficie de QuestPDF (`IDocument`, `.GeneratePdf()`, `.Table()`, `text.CurrentPageNumber()`, etc.) viene de la documentación pública de la librería, no de una prueba en vivo — porque QuestPDF acaba de instalarse en la Task 1 y su API cambia entre versiones mayores. Si el compilador marca un método o propiedad como inexistente, usa el autocompletado del editor sobre el objeto (`container.`, `text.`, `table.`) para encontrar el nombre real en la versión instalada y ajusta — el resto de la estructura (qué va en `Header`/`Content`/`Footer`, qué campos de `PurchaseOrderReportData` usa cada bloque) no depende de la versión exacta.

Crear `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderPdfDocument.cs`:

```csharp
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OCAutomatica.Api.PurchaseOrders;

public sealed class PurchaseOrderPdfDocument : IDocument
{
    private readonly PurchaseOrderReportData _data;

    public PurchaseOrderPdfDocument(PurchaseOrderReportData data) => _data = data;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(30);
            page.Size(PageSizes.Letter);
            page.DefaultTextStyle(x => x.FontSize(9));

            page.Content().Column(column =>
            {
                column.Spacing(10);
                column.Item().Element(ComposeTopBlock); // first page only — nothing else repeats it
                column.Item().Element(ComposeLinesTable);
                column.Item().Element(ComposeTotalsAndComments);
            });

            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeTopBlock(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(6);

            column.Item().Row(row =>
            {
                row.RelativeItem().Text("ORDEN DE COMPRA").Bold().FontSize(14);
                row.ConstantItem(80).AlignRight().Text(_data.Approved ? "APROBADA" : "NO APROBADA").Bold();
            });
            column.Item().Text($"Fecha Impresion: {_data.PrintedAt:dd/MM/yyyy} {_data.PrintedAt:hh:mm}tt").FontSize(8);
            column.Item().Text(_data.PlantName).Bold();
            column.Item().Text($"Orden de Compra: {_data.PoNum}").Bold();

            column.Item().Row(row =>
            {
                row.RelativeItem().Border(1).Padding(6).Column(box =>
                {
                    box.Item().Text($"Proveedor:  {_data.VendorId}").Bold();
                    box.Item().Text(_data.VendorName);
                    box.Item().Text(_data.VendorAddress1);
                    box.Item().Text(_data.VendorAddress2);
                    box.Item().Text(_data.VendorCity);
                });
                row.RelativeItem().Border(1).Padding(6).Column(box =>
                {
                    box.Item().Text("Domicilio de Entrega:").Bold();
                    box.Item().Text(_data.DeliveryAddress);
                });
            });

            column.Item().Border(1).Padding(6).Row(row =>
            {
                row.RelativeItem().Text($"Entrega: {_data.ShipViaDescription}").Bold();
                row.RelativeItem().Text($"Fecha de Orden: {_data.OrderDate:yyyy-MM-dd}").Bold();
            });
        });
    }

    private void ComposeLinesTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(25);   // Linea
                columns.RelativeColumn(3);    // Parte/Rev/Descripcion
                columns.RelativeColumn(1);    // EAN
                columns.RelativeColumn(1);    // Cantidad
                columns.RelativeColumn(1);    // UOM
                columns.RelativeColumn(1);    // Precio Unit
                columns.RelativeColumn(1);    // Precio Ext
            });

            table.Header(header =>
            {
                header.Cell().Text("Linea").Bold();
                header.Cell().Text("Parte\\Rev\\Descripcion").Bold();
                header.Cell().Text("EAN:").Bold();
                header.Cell().AlignRight().Text("Cantidad").Bold();
                header.Cell().Text("UOM").Bold();
                header.Cell().AlignRight().Text("Precio Unit").Bold();
                header.Cell().AlignRight().Text("Precio Ext").Bold();
                header.Cell().ColumnSpan(7).PaddingTop(2).BorderBottom(1);
            });

            foreach (var line in _data.Lines)
            {
                table.Cell().Text(line.Line.ToString());
                table.Cell().Column(col =>
                {
                    col.Item().Text(line.PartNum);
                    col.Item().Text(line.Description);
                });
                table.Cell().Text(line.Ean);
                table.Cell().AlignRight().Text(line.OrderQty.ToString("0.00"));
                table.Cell().Text(line.Uom);
                table.Cell().AlignRight().Text(line.UnitCost.ToString("0.00"));
                table.Cell().AlignRight().Text(line.ExtendedPrice.ToString("0.00"));
            }
        });
    }

    private void ComposeTotalsAndComments(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(4);

            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text($"Autorizado por: {_data.BuyerName}").Bold();
                    left.Item().Text("Solicitado Por:");
                });
                row.RelativeItem().Column(right =>
                {
                    right.Item().AlignRight().Text($"Subtotal Linea(s): {_data.Subtotal:0.00}");
                    right.Item().AlignRight().Text($"Impuestos: {_data.Taxes:0.00}");
                    right.Item().AlignRight().Text($"Retenciones: {_data.Withholdings:0.00}");
                    right.Item().AlignRight().PaddingTop(4).BorderTop(1).Text($"Total: {_data.Total:0.00}").Bold();
                });
            });

            column.Item().PaddingTop(8).Text("Comentarios").Bold().Underline();
            column.Item().Text(string.IsNullOrWhiteSpace(_data.CommentText) ? " " : _data.CommentText);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        // This is literally the Page Footer section of the original Crystal
        // report — confirmed by the user with a screenshot of the report
        // designer — so it repeats on every page (spec section 2.4 / 3).
        container.Column(column =>
        {
            column.Spacing(2);

            column.Item().Row(row =>
            {
                row.RelativeItem().Text("CARNES FINAS SAN JUAN LA FE").Bold();
                row.ConstantItem(100).AlignRight().Text(text =>
                {
                    text.Span("Página ");
                    text.CurrentPageNumber();
                    text.Span(" de ");
                    text.TotalPages();
                });
            });
            column.Item().BorderTop(1);

            column.Item().Text("-Direccion de envió de archivos electronicos: recepcion@buzonfiscal.com.").FontSize(7);
            column.Item().Text("-Es necesario que toda factura de orden de compra recibida por CFSJ, se envie al correo antes mencionado para que proceda el pago de la misma.").FontSize(7);
            column.Item().Text("-La presente orden de compra,cancela los pedidos no entregados con anterioridad.").FontSize(7);
            column.Item().Text(text =>
            {
                text.FontSize(7);
                text.Span("-Favor de confirmar fecha de entrega a ");
                text.Span(string.IsNullOrWhiteSpace(_data.BuyerEmail) ? string.Empty : $"{_data.BuyerEmail} ,");
                text.Span("elvira.reyes@carnessanjuan.com.");
            });
        });
    }
}
```

- [ ] **Step 4: Correr los tests para verificar que pasan**

Run: `dotnet test --filter PurchaseOrderPdfDocumentTests`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderPdfDocument.cs tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderPdfDocumentTests.cs
git commit -m "feat: add PurchaseOrderPdfDocument (QuestPDF layout matching the legacy Crystal report)"
```

---

## Task 6: Endpoint `GET /api/purchase-orders/{poNum}/report`

**Files:**
- Modify: `src/OCAutomatica.Api/Controllers/PurchaseOrdersController.cs`

**Por qué existe esta tarea:** conecta las Tasks 4 y 5 a una URL que el frontend puede abrir directamente en una pestaña nueva.

**Interfaces:**
- Consumes: `IPurchaseOrderReportService.GetReportDataAsync` (Task 4), `PurchaseOrderPdfDocument` (Task 5), `ISessionStore.GetCredentials`, el `HandleEpicorException` ya existente en este controlador.

- [ ] **Step 1: Agregar el endpoint al controlador**

En `src/OCAutomatica.Api/Controllers/PurchaseOrdersController.cs`, agregar el campo, el parámetro de constructor, y el método de acción:

```csharp
using Microsoft.AspNetCore.Mvc;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.PurchaseOrders;

namespace OCAutomatica.Api.Controllers;

[ApiController]
[Route("api/purchase-orders")]
public sealed class PurchaseOrdersController : ControllerBase
{
    private readonly IPurchaseOrderService _purchaseOrders;
    private readonly IPurchaseOrderHistoryService _history;
    private readonly IPurchaseOrderReportService _reports;
    private readonly ISessionStore _sessions;
    private readonly ILogger<PurchaseOrdersController> _logger;

    public PurchaseOrdersController(
        IPurchaseOrderService purchaseOrders,
        IPurchaseOrderHistoryService history,
        IPurchaseOrderReportService reports,
        ISessionStore sessions,
        ILogger<PurchaseOrdersController> logger)
    {
        _purchaseOrders = purchaseOrders;
        _history = history;
        _reports = reports;
        _sessions = sessions;
        _logger = logger;
    }
```

(No agregues todavía un campo/parámetro para `IPurchaseOrderEmailService` — ese tipo no existe hasta la Task 7, y agregarlo ahora rompería la compilación de todo el proyecto — con ella, la de todos los tests de la solución, no solo los de esta Task, porque el proyecto de tests referencia el ensamblado completo de la API. La Task 8 agrega ese campo cuando ya existe y realmente lo necesita.)

Agregar el método de acción, junto a `GetLines`:

```csharp
    [HttpGet("{poNum:int}/report")]
    public async Task<IActionResult> GetReport(int poNum, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        try
        {
            var data = await _reports.GetReportDataAsync(
                session.Company, session.Plant, poNum, session.Username, credentials, ct);

            if (data is null)
                return NotFound(new { message = "No se encontro la orden de compra." });

            var bytes = new PurchaseOrderPdfDocument(data).GeneratePdf();
            return File(bytes, "application/pdf");
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while building the report for PO {PoNum} on {Company}",
                ex.Reason, poNum, session.Company);
            return HandleEpicorException(ex);
        }
    }
```

- [ ] **Step 2: Verificar que la solución completa compila y todos los tests pasan**

Run: `dotnet test`
Expected: todos los tests existentes siguen pasando (este endpoint no tiene tests automatizados propios — sigue la misma convención que el resto de `PurchaseOrdersController`, que tampoco los tiene desde el Plan 3 — la verificación es manual, en el navegador, en la Task 9).

- [ ] **Step 3: Commit**

```bash
git add src/OCAutomatica.Api/Controllers/PurchaseOrdersController.cs
git commit -m "feat: add GET /api/purchase-orders/{poNum}/report endpoint"
```

---

## Task 7: `PurchaseOrderEmailService` — validación y armado del envío de copia

**Files:**
- Create: `src/OCAutomatica.Api/PurchaseOrders/InvalidUserEmailException.cs`
- Create: `src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderEmailService.cs`
- Create: `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderEmailService.cs`
- Modify: `src/OCAutomatica.Api/Program.cs`
- Test: `tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderEmailServiceTests.cs`

**Por qué existe esta tarea:** implementa exactamente lo que hacía `btnEnviarEmail_Click` (spec, sección 2.5) — validar el correo del usuario logueado, resolver `vendorID`/`vendorName` de la OC y el nombre de la planta, y encolar la copia con `oc_tipo = 2` — usando `IEmailQueueRepository` (Task 3) en vez del SP.

**Interfaces:**
- Consumes: `IEpicorClient.GetAsync<T>`, `IUserDirectoryService.GetEmailAsync` (Task 2), `IOrganizationService.GetPlantsAsync`, `IEmailQueueRepository.InsertAsync` (Task 3).
- Produces: `IPurchaseOrderEmailService.SendCopyToUserAsync(string company, string companyName, string plant, string username, int poNum, EpicorCredentials credentials, CancellationToken ct = default)` → `Task<string>` (el correo al que se envió). Lanza `InvalidUserEmailException` si el usuario no tiene un correo válido capturado.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderEmailServiceTests.cs`:

```csharp
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Organization;
using OCAutomatica.Api.PurchaseOrders;
using OCAutomatica.Api.Users;

namespace OCAutomatica.Api.Tests.PurchaseOrders;

public class PurchaseOrderEmailServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly Func<string, object?> _byPath;
        public StubEpicorClient(Func<string, object?> byPath) => _byPath = byPath;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_byPath(relativePath));

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by PurchaseOrderEmailService.");

        public Task<T?> InvokeFunctionAsync<T>(string company, string library, string function,
            object input, EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by PurchaseOrderEmailService.");
    }

    private sealed class StubUserDirectoryService : IUserDirectoryService
    {
        private readonly string? _email;
        public StubUserDirectoryService(string? email) => _email = email;
        public Task<string?> GetEmailAsync(string userId, EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult(_email);
    }

    private sealed class StubOrganizationService : IOrganizationService
    {
        private readonly IReadOnlyList<Plant> _plants;
        public StubOrganizationService(IReadOnlyList<Plant> plants) => _plants = plants;
        public Task<IReadOnlyList<Plant>> GetPlantsAsync(string company, EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult(_plants);
    }

    private sealed class FakeEmailQueueRepository : IEmailQueueRepository
    {
        public EmailQueueEntry? LastEntry { get; private set; }
        public Task InsertAsync(EmailQueueEntry entry, CancellationToken ct = default)
        {
            LastEntry = entry;
            return Task.CompletedTask;
        }
    }

    private static readonly EpicorCredentials Creds = new("epicor", "x");

    private static StubEpicorClient BuildVendorHeaderClient() => new(path =>
    {
        Assert.Contains("POes", path);
        return new PoVendorInfoDto { VendorVendorID = "001008", VendorName = "JARAMILLO TREVIÑO GERARDO MAGDALENO" };
    });

    [Fact]
    public async Task SendCopyToUserAsync_InsertsAQueueEntry_WithOcTipo2AndTheUsersOwnEmail()
    {
        var repository = new FakeEmailQueueRepository();
        var service = new PurchaseOrderEmailService(
            BuildVendorHeaderClient(),
            new StubUserDirectoryService("giovanni.montoya@carnessanjuan.com"),
            new StubOrganizationService(new List<Plant> { new("LAF", "LA FE") }),
            repository);

        var email = await service.SendCopyToUserAsync(
            "CFSJ_LAF", "CARNES FINAS SAN JUAN LA FE", "LAF", "epicor", 3431, Creds);

        Assert.Equal("giovanni.montoya@carnessanjuan.com", email);
        Assert.NotNull(repository.LastEntry);
        Assert.Equal("CFSJ_LAF", repository.LastEntry!.Company);
        Assert.Equal("CARNES FINAS SAN JUAN LA FE", repository.LastEntry.CompanyName);
        Assert.Equal("LAF", repository.LastEntry.Plant);
        Assert.Equal("LA FE", repository.LastEntry.PlantName);
        Assert.Equal("3431", repository.LastEntry.PoNumber);
        Assert.Equal("001008", repository.LastEntry.VendorId);
        Assert.Equal("JARAMILLO TREVIÑO GERARDO MAGDALENO", repository.LastEntry.VendorName);
        Assert.Equal("giovanni.montoya@carnessanjuan.com", repository.LastEntry.Emails);
        Assert.Equal(2, repository.LastEntry.OcTipo);
    }

    [Fact]
    public async Task SendCopyToUserAsync_Throws_WhenUserHasNoEmailCaptured()
    {
        var repository = new FakeEmailQueueRepository();
        var service = new PurchaseOrderEmailService(
            BuildVendorHeaderClient(),
            new StubUserDirectoryService(null),
            new StubOrganizationService(new List<Plant> { new("LAF", "LA FE") }),
            repository);

        await Assert.ThrowsAsync<InvalidUserEmailException>(() =>
            service.SendCopyToUserAsync("CFSJ_LAF", "CARNES FINAS SAN JUAN LA FE", "LAF", "epicor", 3431, Creds));

        Assert.Null(repository.LastEntry);
    }

    [Fact]
    public async Task SendCopyToUserAsync_Throws_WhenUserEmailIsMalformed()
    {
        var repository = new FakeEmailQueueRepository();
        var service = new PurchaseOrderEmailService(
            BuildVendorHeaderClient(),
            new StubUserDirectoryService("no-es-un-correo"),
            new StubOrganizationService(new List<Plant> { new("LAF", "LA FE") }),
            repository);

        await Assert.ThrowsAsync<InvalidUserEmailException>(() =>
            service.SendCopyToUserAsync("CFSJ_LAF", "CARNES FINAS SAN JUAN LA FE", "LAF", "epicor", 3431, Creds));

        Assert.Null(repository.LastEntry);
    }

    [Fact]
    public async Task SendCopyToUserAsync_FallsBackToThePlantCode_WhenPlantNameNotFound()
    {
        var repository = new FakeEmailQueueRepository();
        var service = new PurchaseOrderEmailService(
            BuildVendorHeaderClient(),
            new StubUserDirectoryService("giovanni.montoya@carnessanjuan.com"),
            new StubOrganizationService(Array.Empty<Plant>()),
            repository);

        await service.SendCopyToUserAsync("CFSJ_LAF", "CARNES FINAS SAN JUAN LA FE", "LAF", "epicor", 3431, Creds);

        Assert.Equal("LAF", repository.LastEntry!.PlantName);
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter PurchaseOrderEmailServiceTests`
Expected: FAIL — ninguno de los tipos nuevos existe todavía.

- [ ] **Step 3: Crear la excepción y la interfaz**

Crear `src/OCAutomatica.Api/PurchaseOrders/InvalidUserEmailException.cs`:

```csharp
namespace OCAutomatica.Api.PurchaseOrders;

/// <summary>
/// Thrown when the logged-in user's Epicor record has no email, or an
/// invalid one, captured — mirrors the legacy ValidaEmail check in
/// btnEnviarEmail_Click (spec section 2.5).
/// </summary>
public sealed class InvalidUserEmailException : Exception
{
    public InvalidUserEmailException(string message) : base(message)
    {
    }
}
```

Crear `src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderEmailService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.PurchaseOrders;

public interface IPurchaseOrderEmailService
{
    /// <returns>The email address the copy was sent to.</returns>
    /// <exception cref="InvalidUserEmailException">
    /// The logged-in user has no valid email captured in Epicor.
    /// </exception>
    Task<string> SendCopyToUserAsync(
        string company,
        string companyName,
        string plant,
        string username,
        int poNum,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Implementar el servicio**

Crear `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderEmailService.cs`:

```csharp
using System.Text.RegularExpressions;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Organization;
using OCAutomatica.Api.Users;

namespace OCAutomatica.Api.PurchaseOrders;

public sealed class PurchaseOrderEmailService : IPurchaseOrderEmailService
{
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly IEpicorClient _epicor;
    private readonly IUserDirectoryService _users;
    private readonly IOrganizationService _organization;
    private readonly IEmailQueueRepository _queue;

    public PurchaseOrderEmailService(
        IEpicorClient epicor,
        IUserDirectoryService users,
        IOrganizationService organization,
        IEmailQueueRepository queue)
    {
        _epicor = epicor;
        _users = users;
        _organization = organization;
        _queue = queue;
    }

    public async Task<string> SendCopyToUserAsync(
        string company,
        string companyName,
        string plant,
        string username,
        int poNum,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var email = await _users.GetEmailAsync(username, credentials, ct);
        if (string.IsNullOrWhiteSpace(email) || !EmailPattern.IsMatch(email))
        {
            throw new InvalidUserEmailException(
                "La direccion de email del usuario no esta capturada correctamente, no se puede enviar el email.");
        }

        var vendorPath = $"Erp.BO.POSvc/POes('{company}',{poNum})?$select=VendorVendorID,VendorName";
        var vendor = await _epicor.GetAsync<PoVendorInfoDto>(company, vendorPath, credentials, ct);

        var plants = await _organization.GetPlantsAsync(company, credentials, ct);
        var plantName = plants.FirstOrDefault(p => p.PlantId == plant)?.Name ?? plant;

        var entry = new EmailQueueEntry(
            company,
            companyName,
            plant,
            plantName,
            poNum.ToString(),
            vendor?.VendorVendorID ?? string.Empty,
            vendor?.VendorName ?? string.Empty,
            email,
            2); // oc_tipo=2: copy to the logged-in buyer (spec section 2.5)

        await _queue.InsertAsync(entry, ct);

        return email;
    }
}

public sealed class PoVendorInfoDto
{
    public string VendorVendorID { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Correr los tests para verificar que pasan**

Run: `dotnet test --filter PurchaseOrderEmailServiceTests`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 6: Registrar el servicio en `Program.cs`**

En `src/OCAutomatica.Api/Program.cs`, reemplazar el comentario `// IPurchaseOrderEmailService: registered by Task 7, when it creates the type.` por:

```csharp
builder.Services.AddScoped<IPurchaseOrderEmailService, PurchaseOrderEmailService>();
```

- [ ] **Step 7: Verificar que la solución completa compila y todos los tests pasan**

Run: `dotnet test`
Expected: todos los tests existentes siguen pasando, más los 4 nuevos de esta Task. (`PurchaseOrdersController` todavía no pide `IPurchaseOrderEmailService` en su constructor — eso lo agrega la Task 8, que es cuando realmente lo necesita.)

- [ ] **Step 8: Commit**

```bash
git add src/OCAutomatica.Api/PurchaseOrders/InvalidUserEmailException.cs src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderEmailService.cs src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderEmailService.cs src/OCAutomatica.Api/Program.cs tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderEmailServiceTests.cs
git commit -m "feat: add PurchaseOrderEmailService to send a PO copy to the logged-in buyer"
```

---

## Task 8: Endpoint `POST /api/purchase-orders/{poNum}/send-copy`

**Files:**
- Modify: `src/OCAutomatica.Api/Controllers/PurchaseOrdersController.cs`

**Por qué existe esta tarea:** conecta la Task 7 a una URL que el frontend puede llamar desde el botón "Enviar OC por email al usuario".

**Interfaces:**
- Consumes: `IPurchaseOrderEmailService.SendCopyToUserAsync` (Task 7), `session.AvailableCompanies` (ya existente en `UserSession`).

- [ ] **Step 1: Agregar el campo/parámetro del servicio y el endpoint**

En `src/OCAutomatica.Api/Controllers/PurchaseOrdersController.cs`, agregar el campo y el parámetro de constructor (ahora sí existe `IPurchaseOrderEmailService`, creado en la Task 7):

```csharp
    private readonly IPurchaseOrderService _purchaseOrders;
    private readonly IPurchaseOrderHistoryService _history;
    private readonly IPurchaseOrderReportService _reports;
    private readonly IPurchaseOrderEmailService _emailService;
    private readonly ISessionStore _sessions;
    private readonly ILogger<PurchaseOrdersController> _logger;

    public PurchaseOrdersController(
        IPurchaseOrderService purchaseOrders,
        IPurchaseOrderHistoryService history,
        IPurchaseOrderReportService reports,
        IPurchaseOrderEmailService emailService,
        ISessionStore sessions,
        ILogger<PurchaseOrdersController> logger)
    {
        _purchaseOrders = purchaseOrders;
        _history = history;
        _reports = reports;
        _emailService = emailService;
        _sessions = sessions;
        _logger = logger;
    }
```

Luego agregar el método de acción, junto a `GetReport`:

```csharp
    [HttpPost("{poNum:int}/send-copy")]
    public async Task<IActionResult> SendCopy(int poNum, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        var companyName = session.AvailableCompanies
            .FirstOrDefault(c => c.Company == session.Company)?.CompanyName ?? session.Company;

        try
        {
            var email = await _emailService.SendCopyToUserAsync(
                session.Company, companyName, session.Plant, session.Username, poNum, credentials, ct);

            return Ok(new { message = $"Se envio la OC a tu correo: {email}" });
        }
        catch (InvalidUserEmailException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while sending a PO copy email for PO {PoNum} on {Company}",
                ex.Reason, poNum, session.Company);
            return HandleEpicorException(ex);
        }
    }
```

- [ ] **Step 2: Correr toda la suite de pruebas**

Run: `dotnet test`
Expected: todos los tests pasan (los existentes de antes de este plan, más los 16 nuevos de las Tasks 2, 4, 5 y 7 — no hay tests de integración nuevos para el controlador, siguiendo la misma convención ya establecida para `PurchaseOrdersController` en el Plan 3, donde tampoco los tiene).

- [ ] **Step 3: Commit**

```bash
git add src/OCAutomatica.Api/Controllers/PurchaseOrdersController.cs
git commit -m "feat: add POST /api/purchase-orders/{poNum}/send-copy endpoint"
```

---

## Task 9: Frontend — habilitar "Visualizar OC" y "Enviar OC por email al usuario"

**Files:**
- Modify: `src/web/src/api/client.ts`
- Modify: `src/web/src/purchaseOrders/PurchaseOrderHistory.tsx`

**Por qué existe esta tarea:** conecta los dos endpoints nuevos a los botones que ya existen en la pantalla, deshabilitados desde que se construyó "OCs por proveedor".

**Interfaces:**
- Consumes: `api.purchaseOrders` (ya existente en `client.ts`).
- Produces: `api.purchaseOrders.reportUrl(poNum)` → `string` (no hace un `fetch`, solo construye la URL — la usa `window.open`), `api.purchaseOrders.sendCopy(poNum)` → `Promise<{ message: string }>`.

- [ ] **Step 1: Agregar los dos métodos a `client.ts`**

En `src/web/src/api/client.ts`, dentro de `api.purchaseOrders`, agregar junto a `lines`:

```typescript
  purchaseOrders: {
    create: (vendorId: string, comentarios: string, lineas: PurchaseOrderLine[]) =>
      request<CreatePurchaseOrderResult>('/api/purchase-orders', {
        method: 'POST',
        body: JSON.stringify({ vendorId, comentarios, lineas }),
      }),

    byVendor: (vendorId: string) =>
      request<PurchaseOrderSummary[]>(
        `/api/purchase-orders?vendorId=${encodeURIComponent(vendorId)}`,
      ),

    lines: (poNum: number) =>
      request<PurchaseOrderDetailLine[]>(`/api/purchase-orders/${poNum}/lines`),

    reportUrl: (poNum: number) => `/api/purchase-orders/${poNum}/report`,

    sendCopy: (poNum: number) =>
      request<{ message: string }>(`/api/purchase-orders/${poNum}/send-copy`, {
        method: 'POST',
      }),
  },
```

- [ ] **Step 2: Habilitar los botones en `PurchaseOrderHistory.tsx`**

En `src/web/src/purchaseOrders/PurchaseOrderHistory.tsx`, agregar el estado para el envío de copia y reemplazar los dos botones deshabilitados:

```typescript
export function PurchaseOrderHistory({ vendorId, onBack }: Props) {
  const [orders, setOrders] = useState<PurchaseOrderSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selectedPoNum, setSelectedPoNum] = useState<number | null>(null)
  const [lines, setLines] = useState<PurchaseOrderDetailLine[]>([])
  const [linesLoading, setLinesLoading] = useState(false)
  const [linesError, setLinesError] = useState<string | null>(null)
  const [sendingCopy, setSendingCopy] = useState(false)
  const [sendCopyResult, setSendCopyResult] = useState<{ ok: boolean; message: string } | null>(null)
```

Agregar el manejador, junto a `loadOrders`:

```typescript
  function handleVisualizarOc() {
    if (selectedPoNum === null) return
    window.open(api.purchaseOrders.reportUrl(selectedPoNum), '_blank')
  }

  async function handleEnviarCopia() {
    if (selectedPoNum === null) return
    setSendingCopy(true)
    setSendCopyResult(null)
    try {
      const result = await api.purchaseOrders.sendCopy(selectedPoNum)
      setSendCopyResult({ ok: true, message: result.message })
    } catch (err) {
      setSendCopyResult({
        ok: false,
        message: err instanceof Error ? err.message : 'No se pudo enviar el correo.',
      })
    } finally {
      setSendingCopy(false)
    }
  }
```

Reemplazar los dos botones en el JSX:

```tsx
      <div className="actions-row">
        <button type="button" className="btn-secondary" onClick={loadOrders}>
          Actualizar
        </button>
        <button
          type="button"
          className="btn-secondary"
          disabled={selectedPoNum === null}
          onClick={handleVisualizarOc}
        >
          Visualizar OC
        </button>
        <button
          type="button"
          className="btn-secondary"
          disabled={selectedPoNum === null || sendingCopy}
          onClick={handleEnviarCopia}
        >
          {sendingCopy ? 'Enviando...' : 'Enviar OC por email al usuario'}
        </button>
      </div>

      {sendCopyResult && (
        <p className={sendCopyResult.ok ? 'alert alert-success' : undefined} role={sendCopyResult.ok ? undefined : 'alert'}>
          {sendCopyResult.message}
        </p>
      )}
```

(Se quitan los dos `title="Disponible cuando se integre..."` — ya no aplican.)

- [ ] **Step 3: Verificar en el navegador**

Con el backend corriendo (`dotnet run` en `src/OCAutomatica.Api`) y el frontend (`npm run dev` en `src/web`):

1. Entrar a "OCs por proveedor", seleccionar una OC real, dar clic en "Visualizar OC" — debe abrir una pestaña nueva con el PDF, con los datos reales de esa OC.
2. Dar clic en "Enviar OC por email al usuario" — debe mostrar `"Se envio la OC a tu correo: <tu correo real>"`, y confirmar en SSMS (`SELECT TOP 1 * FROM interfaz_envia_oc ORDER BY id_envia_oc DESC` contra `192.168.100.18`) que la fila nueva tiene `oc_tipo = 2` y el correo correcto.

- [ ] **Step 4: Commit**

```bash
git add src/web/src/api/client.ts src/web/src/purchaseOrders/PurchaseOrderHistory.tsx
git commit -m "feat: wire up Visualizar OC and Enviar OC por email al usuario buttons"
```

---

## Verificación final (antes de dar el plan por completo)

- [ ] `dotnet test` desde la raíz del repo — todo pasa.
- [ ] `npm run build` en `src/web` — compila sin errores de TypeScript.
- [ ] Prueba manual end-to-end descrita en la Task 9, Step 3, con al menos **dos** OCs reales distintas (una con `ShipAddress` capturado y otra sin — para ejercitar ambas ramas de `BuildDeliveryAddress`).
- [ ] Confirmar que `appsettings.Development.json` sigue sin subirse a git: `git status` no debe mostrarlo.
