# OC Automática — Plan 1: Fundación y sesión

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Un comprador abre la aplicación en el navegador, se autentica con sus credenciales de Epicor, elige compañía y planta, y el sistema resuelve su BuyerID desde Buyer Maintenance.

**Architecture:** API en ASP.NET Core 8 que actúa como único intermediario hacia Epicor Kinetic 2026 vía REST v2. Las credenciales del usuario se validan contra Epicor, se cifran con Data Protection y viven en una sesión del lado servidor; el navegador solo recibe una cookie `httpOnly` con el identificador de sesión. Frontend React + TypeScript servido como estáticos por la misma API.

**Tech Stack:** .NET 8, ASP.NET Core Web API, xUnit, React 18, TypeScript, Vite

## Global Constraints

- **.NET 8** (`net8.0`). No usar preview features.
- **Epicor REST v2** únicamente. Ninguna conexión directa a SQL Server en este plan.
- **Ninguna credencial en código ni en `appsettings.json` versionado.** Secretos vía User Secrets en desarrollo y variables de entorno en el servidor.
- **El navegador nunca recibe credenciales de Epicor.** Ni en respuestas, ni en cookies, ni en `localStorage`.
- **NO usar FluentAssertions.** Desde la versión 8 requiere licencia de pago para uso comercial. Usar los asserts de xUnit.
- Ambiente de desarrollo: **el Epicor de pruebas**, nunca producción.
- Mensajes de la interfaz en **español**, sin acentos faltantes.
- Todo el código, nombres de variables y comentarios en **inglés**; solo los textos visibles al usuario van en español.

---

## Estructura de archivos

```
OCAutomatica/
├── OCAutomatica.sln
├── src/
│   ├── OCAutomatica.Api/
│   │   ├── OCAutomatica.Api.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── Epicor/
│   │   │   ├── EpicorCredentials.cs      # record de usuario/contraseña
│   │   │   ├── EpicorOptions.cs          # BaseUrl, ApiKey
│   │   │   ├── EpicorException.cs        # errores tipados (401 vs resto)
│   │   │   ├── IEpicorClient.cs
│   │   │   └── EpicorClient.cs           # HTTP tipado hacia REST v2
│   │   ├── Auth/
│   │   │   ├── UserSession.cs            # datos de la sesión
│   │   │   ├── ISessionStore.cs
│   │   │   ├── SessionStore.cs           # memoria + Data Protection
│   │   │   ├── IAuthService.cs
│   │   │   ├── AuthService.cs            # valida contra Epicor
│   │   │   └── SessionMiddleware.cs      # resuelve la cookie
│   │   ├── Organization/
│   │   │   ├── OrganizationModels.cs     # Company, Plant
│   │   │   ├── IOrganizationService.cs
│   │   │   └── OrganizationService.cs
│   │   ├── Buyers/
│   │   │   ├── BuyerModels.cs
│   │   │   ├── IBuyerService.cs
│   │   │   └── BuyerService.cs
│   │   └── Controllers/
│   │       ├── AuthController.cs
│   │       └── OrganizationController.cs
│   └── web/
│       ├── package.json
│       ├── vite.config.ts
│       └── src/
│           ├── main.tsx
│           ├── App.tsx
│           ├── api/client.ts
│           └── auth/
│               ├── LoginPage.tsx
│               ├── ContextPicker.tsx
│               └── useSession.ts
└── tests/
    └── OCAutomatica.Api.Tests/
        ├── OCAutomatica.Api.Tests.csproj
        ├── Fakes/FakeHttpMessageHandler.cs
        ├── Epicor/EpicorClientTests.cs
        ├── Auth/AuthServiceTests.cs
        ├── Auth/SessionStoreTests.cs
        └── Buyers/BuyerServiceTests.cs
```

**Responsabilidades:**

- `Epicor/` — todo lo que sabe hablar REST v2. Nadie más construye URLs ni encabezados de Epicor.
- `Auth/` — quién es el usuario y cómo se mantiene su sesión.
- `Organization/` — compañías y plantas disponibles.
- `Buyers/` — resolución del BuyerID.
- `Controllers/` — traduce HTTP a llamadas de servicio. Sin lógica de negocio.

---

## Task 1: Esqueleto de la solución

**Files:**
- Create: `OCAutomatica.sln`
- Create: `src/OCAutomatica.Api/OCAutomatica.Api.csproj`
- Create: `src/OCAutomatica.Api/Program.cs`
- Create: `tests/OCAutomatica.Api.Tests/OCAutomatica.Api.Tests.csproj`
- Create: `tests/OCAutomatica.Api.Tests/SmokeTests.cs`
- Create: `.gitignore`

**Interfaces:**
- Consumes: nada
- Produces: solución compilable con `dotnet test` funcionando

- [ ] **Step 1: Crear la solución y los proyectos**

```bash
cd "c:/Users/giovanni.montoya/Proyectos Programacion/OCAutomatica"
dotnet new sln -n OCAutomatica
dotnet new webapi -n OCAutomatica.Api -o src/OCAutomatica.Api --framework net8.0
dotnet new xunit -n OCAutomatica.Api.Tests -o tests/OCAutomatica.Api.Tests --framework net8.0
dotnet sln add src/OCAutomatica.Api/OCAutomatica.Api.csproj
dotnet sln add tests/OCAutomatica.Api.Tests/OCAutomatica.Api.Tests.csproj
dotnet add tests/OCAutomatica.Api.Tests/OCAutomatica.Api.Tests.csproj reference src/OCAutomatica.Api/OCAutomatica.Api.csproj
```

- [ ] **Step 2: Crear `.gitignore`**

Crear `.gitignore` en la raíz:

```gitignore
bin/
obj/
node_modules/
dist/
.vs/
*.user
appsettings.Development.json
appsettings.Production.json
```

- [ ] **Step 3: Escribir el test de humo**

Crear `tests/OCAutomatica.Api.Tests/SmokeTests.cs`:

```csharp
namespace OCAutomatica.Api.Tests;

public class SmokeTests
{
    [Fact]
    public void TestProjectRuns()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 4: Verificar que compila y los tests corren**

Run: `dotnet test`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "chore: scaffold solution with API and test projects"
```

---

## Task 2: Cliente REST de Epicor

**Files:**
- Create: `src/OCAutomatica.Api/Epicor/EpicorCredentials.cs`
- Create: `src/OCAutomatica.Api/Epicor/EpicorOptions.cs`
- Create: `src/OCAutomatica.Api/Epicor/EpicorException.cs`
- Create: `src/OCAutomatica.Api/Epicor/IEpicorClient.cs`
- Create: `src/OCAutomatica.Api/Epicor/EpicorClient.cs`
- Create: `tests/OCAutomatica.Api.Tests/Fakes/FakeHttpMessageHandler.cs`
- Test: `tests/OCAutomatica.Api.Tests/Epicor/EpicorClientTests.cs`

**Interfaces:**
- Consumes: nada
- Produces:
  - `record EpicorCredentials(string Username, string Password)`
  - `class EpicorOptions { string BaseUrl; string ApiKey; }`
  - `enum EpicorErrorReason { InvalidCredentials, InvalidApiKey, AccessDenied, Other }`
  - `class EpicorException : Exception { int StatusCode; EpicorErrorReason Reason; }`
  - `interface IEpicorClient`
    - `Task<T?> GetAsync<T>(string company, string relativePath, EpicorCredentials creds, CancellationToken ct = default)`
    - `Task<T?> PostAsync<T>(string company, string relativePath, object body, EpicorCredentials creds, CancellationToken ct = default)`

- [ ] **Step 1: Verificar manualmente los endpoints contra el Epicor de pruebas**

**Ya verificado contra el ambiente de pruebas real.** Resultados:

> URL base de pruebas: `https://srvcsjpr2.carnessanjuan.local/Kinetic2026_1`
> (la ruta completa de un recurso es `{BaseUrl}/api/v2/odata/{Company}/{Servicio}/{Recurso}`)

**Hallazgo importante: Epicor responde HTTP 401 para tres situaciones distintas**, diferenciadas únicamente por el texto de `ErrorMessage` en el cuerpo de la respuesta — nunca por el código de estado:

| Situación | `ErrorMessage` observado |
|---|---|
| Contraseña incorrecta | `"Invalid username or password."` |
| API key incorrecto | `"Invalid API Key {key}.\r\nCompany {company}."` |
| Usuario válido sin permiso a ese Business Object | `"Access denied ({BO}.{Método})."` |

Ejemplo real capturado (contraseña incorrecta contra `Erp.BO.VendorSvc/Vendors`):

```json
{"HttpStatus":401,"ReasonPhrase":"REST API Exception","ErrorMessage":"Invalid username or password.","ErrorType":"System.UnauthorizedAccessException","CorrelationId":"41893675-a27f-467a-be02-7af2ed7d708c"}
```

Esto significa que `EpicorException` no puede clasificar el error solo con el código HTTP — debe leer `ErrorMessage` del cuerpo. El diseño original de este plan (una sola propiedad `IsUnauthorized`) habría reportado "contraseña incorrecta" ante un simple problema de permisos. Los pasos de implementación de esta tarea ya incorporan la corrección.

**También confirmado: `Erp.BO.CompanySvc` existe pero el usuario de pruebas no tiene permiso** (`Access denied (Erp.BO.Company.GetRows)`) — es una restricción de seguridad de Epicor sobre ese usuario, no un problema de Access Scope del API key. Por eso el "ping" de validación de credenciales (Task 3) usa `Erp.BO.VendorSvc/Vendors` en lugar de `CompanySvc`: todo comprador que use esta aplicación necesariamente tiene permiso de lectura sobre proveedores, así que es un mínimo razonable y además semánticamente relevante para esta app.

- [ ] **Step 2: Escribir el fake de HttpMessageHandler**

Crear `tests/OCAutomatica.Api.Tests/Fakes/FakeHttpMessageHandler.cs`:

```csharp
using System.Net;

namespace OCAutomatica.Api.Tests.Fakes;

public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpMessageHandler(HttpStatusCode status, string body)
    {
        _status = status;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var response = new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
```

- [ ] **Step 3: Escribir los tests que fallan**

Crear `tests/OCAutomatica.Api.Tests/Epicor/EpicorClientTests.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Options;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Tests.Fakes;

namespace OCAutomatica.Api.Tests.Epicor;

public class EpicorClientTests
{
    private sealed record Company(string Company1, string Name);
    private sealed record ODataList<T>(List<T> Value);

    private static EpicorClient BuildClient(FakeHttpMessageHandler handler)
    {
        var options = Options.Create(new EpicorOptions
        {
            BaseUrl = "https://epicor-test/erp102600v2",
            ApiKey = "test-api-key"
        });
        var httpClient = new HttpClient(handler);
        return new EpicorClient(httpClient, options);
    }

    [Fact]
    public async Task GetAsync_BuildsUrlWithCompanyAndPath()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, """{"Value":[]}""");
        var client = BuildClient(handler);

        await client.GetAsync<ODataList<Company>>(
            "CFSJ_LAF",
            "Erp.BO.CompanySvc/Companies",
            new EpicorCredentials("user", "pass"));

        Assert.Equal(
            "https://epicor-test/erp102600v2/api/v2/odata/CFSJ_LAF/Erp.BO.CompanySvc/Companies",
            handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetAsync_SendsBasicAuthAndApiKey()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, """{"Value":[]}""");
        var client = BuildClient(handler);

        await client.GetAsync<ODataList<Company>>(
            "CFSJ_LAF",
            "Erp.BO.CompanySvc/Companies",
            new EpicorCredentials("jyanez", "secreto"));

        var request = handler.LastRequest!;
        var expectedAuth = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("jyanez:secreto"));

        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal(expectedAuth, request.Headers.Authorization.Parameter);
        Assert.Equal("test-api-key", request.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task GetAsync_DeserializesResponse()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"Value":[{"Company1":"CFSJ_LAF","Name":"CARNES FINAS SAN JUAN LA FE"}]}""");
        var client = BuildClient(handler);

        var result = await client.GetAsync<ODataList<Company>>(
            "CFSJ_LAF",
            "Erp.BO.CompanySvc/Companies",
            new EpicorCredentials("user", "pass"));

        Assert.NotNull(result);
        Assert.Single(result!.Value);
        Assert.Equal("CFSJ_LAF", result.Value[0].Company1);
    }

    [Fact]
    public async Task GetAsync_ClassifiesInvalidCredentials()
    {
        // Real Epicor response body, captured against the test environment.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized,
            """{"HttpStatus":401,"ReasonPhrase":"REST API Exception","ErrorMessage":"Invalid username or password.","ErrorType":"System.UnauthorizedAccessException","CorrelationId":"41893675-a27f-467a-be02-7af2ed7d708c"}""");
        var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<EpicorException>(() =>
            client.GetAsync<ODataList<Company>>(
                "CFSJ_LAF",
                "Erp.BO.VendorSvc/Vendors",
                new EpicorCredentials("user", "malapass")));

        Assert.Equal(EpicorErrorReason.InvalidCredentials, ex.Reason);
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task GetAsync_ClassifiesInvalidApiKey()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized,
            """{"HttpStatus":401,"ReasonPhrase":"REST API Exception","ErrorMessage":"Invalid API Key llave-invalida.\r\nCompany CFSJ_LAF.","ErrorType":"System.UnauthorizedAccessException","CorrelationId":"96d27258-a13c-4b16-8a51-6ce0d28a5619"}""");
        var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<EpicorException>(() =>
            client.GetAsync<ODataList<Company>>(
                "CFSJ_LAF",
                "Erp.BO.VendorSvc/Vendors",
                new EpicorCredentials("user", "pass")));

        Assert.Equal(EpicorErrorReason.InvalidApiKey, ex.Reason);
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task GetAsync_ClassifiesAccessDenied()
    {
        // A user can authenticate successfully and still lack rights to a
        // specific Business Object (Epicor security, independent of the
        // password being correct). Must not be confused with bad credentials.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized,
            """{"HttpStatus":401,"ReasonPhrase":"REST API Exception","ErrorMessage":"Access denied (Erp.BO.Company.GetRows).","ErrorType":"System.UnauthorizedAccessException","CorrelationId":"1d6f1747-acae-4391-9f75-b04f61229a6d"}""");
        var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<EpicorException>(() =>
            client.GetAsync<ODataList<Company>>(
                "CFSJ_LAF",
                "Erp.BO.CompanySvc/Companies",
                new EpicorCredentials("user", "correcta")));

        Assert.Equal(EpicorErrorReason.AccessDenied, ex.Reason);
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task GetAsync_ClassifiesUnrecognizedFailureAsOther()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.InternalServerError, "boom");
        var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<EpicorException>(() =>
            client.GetAsync<ODataList<Company>>(
                "CFSJ_LAF",
                "Erp.BO.CompanySvc/Companies",
                new EpicorCredentials("user", "pass")));

        Assert.Equal(EpicorErrorReason.Other, ex.Reason);
        Assert.Equal(500, ex.StatusCode);
    }
}
```

- [ ] **Step 4: Correr los tests para verificar que fallan**

Run: `dotnet test --filter EpicorClientTests`
Expected: FAIL — no compila, los tipos `EpicorClient`, `EpicorOptions`, `EpicorCredentials`, `EpicorException`, `EpicorErrorReason` no existen

- [ ] **Step 5: Implementar los tipos de apoyo**

Crear `src/OCAutomatica.Api/Epicor/EpicorCredentials.cs`:

```csharp
namespace OCAutomatica.Api.Epicor;

/// <summary>
/// Epicor user credentials. Never serialize this type into an HTTP response.
/// </summary>
public sealed record EpicorCredentials(string Username, string Password);
```

Crear `src/OCAutomatica.Api/Epicor/EpicorOptions.cs`:

```csharp
namespace OCAutomatica.Api.Epicor;

public sealed class EpicorOptions
{
    public const string SectionName = "Epicor";

    /// <summary>Base URL without trailing slash, e.g. https://server/erp102600v2</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;
}
```

Crear `src/OCAutomatica.Api/Epicor/EpicorException.cs`:

```csharp
namespace OCAutomatica.Api.Epicor;

/// <summary>
/// Epicor returns HTTP 401 for three unrelated situations, distinguished only
/// by the ErrorMessage text in the response body: bad credentials, a bad
/// x-api-key, and a valid user denied access to a specific Business Object.
/// Callers must not treat every 401 as "wrong password".
/// </summary>
public enum EpicorErrorReason
{
    InvalidCredentials,
    InvalidApiKey,
    AccessDenied,
    Other
}

public sealed class EpicorException : Exception
{
    public int StatusCode { get; }
    public EpicorErrorReason Reason { get; }

    public EpicorException(int statusCode, EpicorErrorReason reason, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Reason = reason;
    }
}
```

Crear `src/OCAutomatica.Api/Epicor/IEpicorClient.cs`:

```csharp
namespace OCAutomatica.Api.Epicor;

public interface IEpicorClient
{
    Task<T?> GetAsync<T>(
        string company,
        string relativePath,
        EpicorCredentials credentials,
        CancellationToken ct = default);

    Task<T?> PostAsync<T>(
        string company,
        string relativePath,
        object body,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
```

- [ ] **Step 6: Implementar `EpicorClient`**

Crear `src/OCAutomatica.Api/Epicor/EpicorClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OCAutomatica.Api.Epicor;

public sealed class EpicorClient : IEpicorClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly EpicorOptions _options;

    public EpicorClient(HttpClient httpClient, IOptions<EpicorOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public Task<T?> GetAsync<T>(
        string company,
        string relativePath,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Get, company, relativePath, credentials);
        return SendAsync<T>(request, ct);
    }

    public Task<T?> PostAsync<T>(
        string company,
        string relativePath,
        object body,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Post, company, relativePath, credentials);
        request.Content = JsonContent.Create(body);
        return SendAsync<T>(request, ct);
    }

    private HttpRequestMessage BuildRequest(
        HttpMethod method,
        string company,
        string relativePath,
        EpicorCredentials credentials)
    {
        var url = $"{_options.BaseUrl}/api/v2/odata/{company}/{relativePath}";
        var request = new HttpRequestMessage(method, url);

        var token = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Add("x-api-key", _options.ApiKey);

        return request;
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw BuildException((int)response.StatusCode, body);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static EpicorException BuildException(int statusCode, string body)
    {
        string? errorMessage = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<EpicorErrorBody>(body, JsonOptions);
            errorMessage = parsed?.ErrorMessage;
        }
        catch (JsonException)
        {
            // Body was not Epicor's structured error shape; fall through with the raw text.
        }

        var reason = ClassifyReason(statusCode, errorMessage);
        var message = errorMessage ?? $"Epicor responded {statusCode}: {body}";
        return new EpicorException(statusCode, reason, message);
    }

    private static EpicorErrorReason ClassifyReason(int statusCode, string? errorMessage)
    {
        if (statusCode != 401 || errorMessage is null) return EpicorErrorReason.Other;

        if (errorMessage.StartsWith("Invalid username or password", StringComparison.OrdinalIgnoreCase))
            return EpicorErrorReason.InvalidCredentials;

        if (errorMessage.StartsWith("Invalid API Key", StringComparison.OrdinalIgnoreCase))
            return EpicorErrorReason.InvalidApiKey;

        if (errorMessage.StartsWith("Access denied", StringComparison.OrdinalIgnoreCase))
            return EpicorErrorReason.AccessDenied;

        return EpicorErrorReason.Other;
    }

    private sealed class EpicorErrorBody
    {
        public string? ErrorMessage { get; set; }
    }
}
```

- [ ] **Step 7: Correr los tests para verificar que pasan**

Run: `dotnet test --filter EpicorClientTests`
Expected: `Passed! - Failed: 0, Passed: 7`

- [ ] **Step 8: Commit**

```bash
git add src/OCAutomatica.Api/Epicor tests/OCAutomatica.Api.Tests
git commit -m "feat: add typed Epicor REST v2 client with auth headers and error mapping"
```

---

## Task 3: Validación de credenciales contra Epicor

**Files:**
- Create: `src/OCAutomatica.Api/Auth/IAuthService.cs`
- Create: `src/OCAutomatica.Api/Auth/AuthService.cs`
- Test: `tests/OCAutomatica.Api.Tests/Auth/AuthServiceTests.cs`

**Interfaces:**
- Consumes: `IEpicorClient`, `EpicorCredentials`, `EpicorException`
- Produces:
  - `interface IAuthService { Task<bool> ValidateAsync(EpicorCredentials creds, string company, CancellationToken ct = default) }`

**Nota de diseño:** la validación se hace con una consulta trivial a `Erp.BO.VendorSvc/Vendors` (no `CompanySvc` — el usuario de pruebas no tiene permiso ahí por seguridad de Epicor, no por credenciales; ver el hallazgo de Task 2). Si Epicor responde 200, las credenciales sirven. Si responde con `EpicorErrorReason.InvalidCredentials`, no. **Cualquier otro motivo se propaga** — un API key inválido o un usuario sin permiso a Vendors no deben presentarse al usuario como "contraseña incorrecta". Este es exactamente el defecto 10.7 del spec, evitado desde el diseño.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/OCAutomatica.Api.Tests/Auth/AuthServiceTests.cs`:

```csharp
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Tests.Auth;

public class AuthServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly Func<object?> _behavior;

        public StubEpicorClient(Func<object?> behavior) => _behavior = behavior;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_behavior());

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_behavior());
    }

    [Fact]
    public async Task ValidateAsync_ReturnsTrueWhenEpicorAccepts()
    {
        var client = new StubEpicorClient(() => new object());
        var service = new AuthService(client);

        var result = await service.ValidateAsync(
            new EpicorCredentials("jyanez", "correcta"), "CFSJ_LAF");

        Assert.True(result);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsFalseOnInvalidCredentials()
    {
        var client = new StubEpicorClient(() => throw new EpicorException(
            401, EpicorErrorReason.InvalidCredentials, "Invalid username or password."));
        var service = new AuthService(client);

        var result = await service.ValidateAsync(
            new EpicorCredentials("jyanez", "incorrecta"), "CFSJ_LAF");

        Assert.False(result);
    }

    [Fact]
    public async Task ValidateAsync_PropagatesAccessDenied()
    {
        // The password can be correct while the user still lacks rights to
        // Erp.BO.VendorSvc in Epicor's own security. That is not a login
        // failure and must not be reported as one.
        var client = new StubEpicorClient(() => throw new EpicorException(
            401, EpicorErrorReason.AccessDenied, "Access denied (Erp.BO.Vendor.GetRows)."));
        var service = new AuthService(client);

        var ex = await Assert.ThrowsAsync<EpicorException>(() =>
            service.ValidateAsync(new EpicorCredentials("jyanez", "correcta"), "CFSJ_LAF"));

        Assert.Equal(EpicorErrorReason.AccessDenied, ex.Reason);
    }

    [Fact]
    public async Task ValidateAsync_PropagatesOtherErrors()
    {
        var client = new StubEpicorClient(() => throw new EpicorException(
            503, EpicorErrorReason.Other, "service unavailable"));
        var service = new AuthService(client);

        var ex = await Assert.ThrowsAsync<EpicorException>(() =>
            service.ValidateAsync(new EpicorCredentials("jyanez", "x"), "CFSJ_LAF"));

        Assert.Equal(503, ex.StatusCode);
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter AuthServiceTests`
Expected: FAIL — `AuthService` no existe

- [ ] **Step 3: Implementar la interfaz**

Crear `src/OCAutomatica.Api/Auth/IAuthService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Auth;

public interface IAuthService
{
    /// <summary>
    /// Returns true when Epicor accepts the credentials, false when it rejects them.
    /// Any other failure (network, server error) propagates as EpicorException so it
    /// is never reported to the user as a wrong password.
    /// </summary>
    Task<bool> ValidateAsync(
        EpicorCredentials credentials,
        string company,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Implementar `AuthService`**

Crear `src/OCAutomatica.Api/Auth/AuthService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Auth;

public sealed class AuthService : IAuthService
{
    // Every buyer using this application must be able to read Vendors in
    // Epicor — it is the app's core purpose. CompanySvc was tried first and
    // rejected for the test user with an Access Scope/security error, which
    // is unrelated to whether the password is correct.
    private const string ProbePath = "Erp.BO.VendorSvc/Vendors?$top=1";

    private readonly IEpicorClient _epicor;

    public AuthService(IEpicorClient epicor) => _epicor = epicor;

    public async Task<bool> ValidateAsync(
        EpicorCredentials credentials,
        string company,
        CancellationToken ct = default)
    {
        try
        {
            await _epicor.GetAsync<object>(company, ProbePath, credentials, ct);
            return true;
        }
        catch (EpicorException ex) when (ex.Reason == EpicorErrorReason.InvalidCredentials)
        {
            return false;
        }
    }
}
```

- [ ] **Step 5: Correr los tests para verificar que pasan**

Run: `dotnet test --filter AuthServiceTests`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 6: Commit**

```bash
git add src/OCAutomatica.Api/Auth tests/OCAutomatica.Api.Tests/Auth
git commit -m "feat: validate user credentials against Epicor without masking infrastructure errors"
```

---

## Task 4: Almacén de sesiones cifrado

**Files:**
- Create: `src/OCAutomatica.Api/Auth/UserSession.cs`
- Create: `src/OCAutomatica.Api/Auth/ISessionStore.cs`
- Create: `src/OCAutomatica.Api/Auth/SessionStore.cs`
- Test: `tests/OCAutomatica.Api.Tests/Auth/SessionStoreTests.cs`

**Interfaces:**
- Consumes: `EpicorCredentials`
- Produces:
  - `class UserSession { string SessionId; string Username; string Company; string Plant; string? BuyerId; DateTimeOffset LastSeenUtc; }`
  - `interface ISessionStore`
    - `string Create(EpicorCredentials creds)`
    - `UserSession? Get(string sessionId)`
    - `EpicorCredentials? GetCredentials(string sessionId)`
    - `void SetContext(string sessionId, string company, string plant, string? buyerId)`
    - `void Remove(string sessionId)`

**Nota de diseño:** las contraseñas se guardan protegidas con la Data Protection API, no en texto plano, incluso estando en memoria. `UserSession` **no** expone la contraseña; solo `GetCredentials` la descifra, y ese método nunca se llama desde un controlador.

**Limitación conocida:** el almacén vive en memoria. Cuando IIS recicle el pool de aplicaciones, las sesiones se pierden y los usuarios vuelven a autenticarse. Para 5–20 compradores en LAN es aceptable. `ISessionStore` existe precisamente para poder cambiarlo por uno persistente sin tocar el resto.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/OCAutomatica.Api.Tests/Auth/SessionStoreTests.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Tests.Auth;

public class SessionStoreTests
{
    private static SessionStore BuildStore()
    {
        var provider = DataProtectionProvider.Create("OCAutomatica.Tests");
        return new SessionStore(provider, TimeProvider.System);
    }

    [Fact]
    public void Create_ReturnsNonEmptySessionId()
    {
        var store = BuildStore();

        var sessionId = store.Create(new EpicorCredentials("jyanez", "secreto"));

        Assert.False(string.IsNullOrWhiteSpace(sessionId));
    }

    [Fact]
    public void Create_GeneratesDistinctIdsForEachSession()
    {
        var store = BuildStore();

        var first = store.Create(new EpicorCredentials("jyanez", "secreto"));
        var second = store.Create(new EpicorCredentials("jyanez", "secreto"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Get_ReturnsSessionWithUsername()
    {
        var store = BuildStore();
        var sessionId = store.Create(new EpicorCredentials("jyanez", "secreto"));

        var session = store.Get(sessionId);

        Assert.NotNull(session);
        Assert.Equal("jyanez", session!.Username);
    }

    [Fact]
    public void Get_ReturnsNullForUnknownSession()
    {
        var store = BuildStore();

        var session = store.Get("no-existe");

        Assert.Null(session);
    }

    [Fact]
    public void GetCredentials_RoundTripsPassword()
    {
        var store = BuildStore();
        var sessionId = store.Create(new EpicorCredentials("jyanez", "secreto"));

        var credentials = store.GetCredentials(sessionId);

        Assert.NotNull(credentials);
        Assert.Equal("jyanez", credentials!.Username);
        Assert.Equal("secreto", credentials.Password);
    }

    [Fact]
    public void SetContext_StoresCompanyPlantAndBuyer()
    {
        var store = BuildStore();
        var sessionId = store.Create(new EpicorCredentials("jyanez", "secreto"));

        store.SetContext(sessionId, "CFSJ_LAF", "MfgSys", "LAF-CM5");
        var session = store.Get(sessionId);

        Assert.Equal("CFSJ_LAF", session!.Company);
        Assert.Equal("MfgSys", session.Plant);
        Assert.Equal("LAF-CM5", session.BuyerId);
    }

    [Fact]
    public void Remove_DeletesTheSession()
    {
        var store = BuildStore();
        var sessionId = store.Create(new EpicorCredentials("jyanez", "secreto"));

        store.Remove(sessionId);

        Assert.Null(store.Get(sessionId));
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter SessionStoreTests`
Expected: FAIL — `SessionStore` no existe

- [ ] **Step 3: Implementar `UserSession` e `ISessionStore`**

Crear `src/OCAutomatica.Api/Auth/UserSession.cs`:

```csharp
namespace OCAutomatica.Api.Auth;

/// <summary>
/// Server-side session state. Deliberately does not expose the password —
/// only ISessionStore.GetCredentials can decrypt it.
/// </summary>
public sealed class UserSession
{
    public required string SessionId { get; init; }
    public required string Username { get; init; }
    public string Company { get; set; } = string.Empty;
    public string Plant { get; set; } = string.Empty;
    public string? BuyerId { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}
```

Crear `src/OCAutomatica.Api/Auth/ISessionStore.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Auth;

public interface ISessionStore
{
    string Create(EpicorCredentials credentials);
    UserSession? Get(string sessionId);
    EpicorCredentials? GetCredentials(string sessionId);
    void SetContext(string sessionId, string company, string plant, string? buyerId);
    void Remove(string sessionId);
}
```

- [ ] **Step 4: Implementar `SessionStore`**

Crear `src/OCAutomatica.Api/Auth/SessionStore.cs`:

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Auth;

public sealed class SessionStore : ISessionStore
{
    private const string ProtectorPurpose = "OCAutomatica.SessionCredentials";

    private sealed record Entry(UserSession Session, string ProtectedPassword);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;

    public SessionStore(IDataProtectionProvider protectionProvider, TimeProvider time)
    {
        _protector = protectionProvider.CreateProtector(ProtectorPurpose);
        _time = time;
    }

    public string Create(EpicorCredentials credentials)
    {
        var sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var session = new UserSession
        {
            SessionId = sessionId,
            Username = credentials.Username,
            LastSeenUtc = _time.GetUtcNow()
        };

        _entries[sessionId] = new Entry(session, _protector.Protect(credentials.Password));
        return sessionId;
    }

    public UserSession? Get(string sessionId)
    {
        if (!_entries.TryGetValue(sessionId, out var entry)) return null;

        entry.Session.LastSeenUtc = _time.GetUtcNow();
        return entry.Session;
    }

    public EpicorCredentials? GetCredentials(string sessionId)
    {
        if (!_entries.TryGetValue(sessionId, out var entry)) return null;

        var password = _protector.Unprotect(entry.ProtectedPassword);
        return new EpicorCredentials(entry.Session.Username, password);
    }

    public void SetContext(string sessionId, string company, string plant, string? buyerId)
    {
        if (!_entries.TryGetValue(sessionId, out var entry)) return;

        entry.Session.Company = company;
        entry.Session.Plant = plant;
        entry.Session.BuyerId = buyerId;
    }

    public void Remove(string sessionId) => _entries.TryRemove(sessionId, out _);
}
```

- [ ] **Step 5: Correr los tests para verificar que pasan**

Run: `dotnet test --filter SessionStoreTests`
Expected: `Passed! - Failed: 0, Passed: 7`

- [ ] **Step 6: Commit**

```bash
git add src/OCAutomatica.Api/Auth tests/OCAutomatica.Api.Tests/Auth
git commit -m "feat: add server-side session store with Data Protection for credentials"
```

---

## Task 5: Resolución del BuyerID

**Files:**
- Create: `src/OCAutomatica.Api/Buyers/BuyerModels.cs`
- Create: `src/OCAutomatica.Api/Buyers/IBuyerService.cs`
- Create: `src/OCAutomatica.Api/Buyers/BuyerService.cs`
- Test: `tests/OCAutomatica.Api.Tests/Buyers/BuyerServiceTests.cs`

**Interfaces:**
- Consumes: `IEpicorClient`, `EpicorCredentials`
- Produces:
  - `record Buyer(string BuyerId, string Name)`
  - `interface IBuyerService { Task<Buyer?> ResolveDefaultBuyerAsync(string company, string username, EpicorCredentials creds, CancellationToken ct = default) }`

**Regla de negocio (spec sección 5):** el BuyerID de una orden nueva es aquel donde el usuario de la sesión está marcado como **Default Buyer**. Si no tiene ninguno, el servicio devuelve `null` y la aplicación bloquea la creación de órdenes en vez de inventar un comprador. Esto reemplaza el `"LNC-CM2"` fijo del código actual, que podía atribuir órdenes a compradores de otra sucursal.

- [ ] **Step 1: Verificar la forma real del servicio de Buyer Maintenance**

**Ya verificado contra el ambiente de pruebas.** `Erp.BO.BuyerSvc` **no existe** — el nombre real del servicio es **`Erp.BO.PurAgentSvc`**, y la tabla que respalda a "Buyer" es `PurAgent`. Consulta usada:

```bash
curl -sk -u "USUARIO:CONTRASENA" -H "x-api-key: TU_API_KEY" \
  "https://srvcsjpr2.carnessanjuan.local/Kinetic2026_1/api/v2/odata/CFSJ_LAF/Erp.BO.PurAgentSvc/PurAgents?\$filter=BuyerID%20eq%20%27LNC-CM2%27&\$expand=PurAuths"
```

Respuesta real (con el usuario de pruebas `epicor` ya asignado como comprador por defecto de `LNC-CM2`):

```json
{"value":[{"BuyerID":"LNC-CM2","Name":"OC AUTOMATICA","PurAuths":[
  {"DcdUserID":"epicor","IsPrimaryUser":true,"Name":"...","BuyerID":"LNC-CM2","Company":"CFSJ_LAF"}
]}]}
```

Nombres confirmados:
- Colección hija de usuarios autorizados: **`PurAuths`** (no `BuyerAuth`)
- Campo que indica Default Buyer: **`IsPrimaryUser`** (no `DefaultBuyer`) — corresponde al checkbox "Default Buyer" de Buyer Maintenance
- Campo con el usuario de Epicor: **`DcdUserID`** (esto sí coincidía con la suposición original)

El DTO del paso 3 ya usa los nombres correctos.

- [ ] **Step 2: Escribir los tests que fallan**

Crear `tests/OCAutomatica.Api.Tests/Buyers/BuyerServiceTests.cs`:

```csharp
using OCAutomatica.Api.Buyers;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Tests.Buyers;

public class BuyerServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly object? _response;

        public StubEpicorClient(object? response) => _response = response;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_response);

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_response);
    }

    private static BuyerListResponse Response(params BuyerDto[] buyers)
        => new() { Value = buyers.ToList() };

    private static BuyerDto Buyer(string id, string name, params (string User, bool IsDefault)[] auth)
        => new()
        {
            BuyerID = id,
            Name = name,
            PurAuths = auth
                .Select(a => new PurAuthDto { DcdUserID = a.User, IsPrimaryUser = a.IsDefault })
                .ToList()
        };

    private static readonly EpicorCredentials Creds = new("jyanez", "x");

    [Fact]
    public async Task ResolveDefaultBuyerAsync_ReturnsBuyerWhereUserIsDefault()
    {
        var client = new StubEpicorClient(Response(
            Buyer("LAF-CM5", "ESTEFANY JUAREZ", ("nestefany", true), ("jyanez", false)),
            Buyer("LAF-CM9", "JANNETH YANEZ", ("jyanez", true))));
        var service = new BuyerService(client);

        var buyer = await service.ResolveDefaultBuyerAsync("CFSJ_LAF", "jyanez", Creds);

        Assert.NotNull(buyer);
        Assert.Equal("LAF-CM9", buyer!.BuyerId);
        Assert.Equal("JANNETH YANEZ", buyer.Name);
    }

    [Fact]
    public async Task ResolveDefaultBuyerAsync_ReturnsNullWhenUserIsOnlyAuthorized()
    {
        // jyanez puede editar ordenes de LAF-CM5 pero no es su Default Buyer,
        // por lo tanto no puede crear ordenes a nombre de ese comprador.
        var client = new StubEpicorClient(Response(
            Buyer("LAF-CM5", "ESTEFANY JUAREZ", ("nestefany", true), ("jyanez", false))));
        var service = new BuyerService(client);

        var buyer = await service.ResolveDefaultBuyerAsync("CFSJ_LAF", "jyanez", Creds);

        Assert.Null(buyer);
    }

    [Fact]
    public async Task ResolveDefaultBuyerAsync_ReturnsNullWhenUserHasNoBuyer()
    {
        var client = new StubEpicorClient(Response(
            Buyer("LAF-CM5", "ESTEFANY JUAREZ", ("nestefany", true))));
        var service = new BuyerService(client);

        var buyer = await service.ResolveDefaultBuyerAsync("CFSJ_LAF", "otro", Creds);

        Assert.Null(buyer);
    }

    [Fact]
    public async Task ResolveDefaultBuyerAsync_IgnoresUsernameCasing()
    {
        var client = new StubEpicorClient(Response(
            Buyer("LAF-CM9", "JANNETH YANEZ", ("JYanez", true))));
        var service = new BuyerService(client);

        var buyer = await service.ResolveDefaultBuyerAsync("CFSJ_LAF", "jyanez", Creds);

        Assert.NotNull(buyer);
        Assert.Equal("LAF-CM9", buyer!.BuyerId);
    }

    [Fact]
    public async Task ResolveDefaultBuyerAsync_ReturnsNullWhenNoBuyersExist()
    {
        var client = new StubEpicorClient(Response());
        var service = new BuyerService(client);

        var buyer = await service.ResolveDefaultBuyerAsync("CFSJ_LAF", "jyanez", Creds);

        Assert.Null(buyer);
    }
}
```

- [ ] **Step 3: Implementar los modelos**

Crear `src/OCAutomatica.Api/Buyers/BuyerModels.cs`:

```csharp
namespace OCAutomatica.Api.Buyers;

/// <summary>Buyer as exposed to the rest of the application.</summary>
public sealed record Buyer(string BuyerId, string Name);

// DTOs matching the Erp.BO.PurAgentSvc payload, confirmed against the test
// environment (see Task 5, Step 1). PurAgent is the underlying table for
// Buyer Maintenance; PurAuth is its "Authorized Users" child table.

public sealed class BuyerListResponse
{
    public List<BuyerDto> Value { get; set; } = new();
}

public sealed class BuyerDto
{
    public string BuyerID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<PurAuthDto> PurAuths { get; set; } = new();
}

public sealed class PurAuthDto
{
    public string DcdUserID { get; set; } = string.Empty;

    /// <summary>Backs the "Default Buyer" checkbox in Buyer Maintenance's Authorized Users tab.</summary>
    public bool IsPrimaryUser { get; set; }
}
```

- [ ] **Step 4: Implementar la interfaz**

Crear `src/OCAutomatica.Api/Buyers/IBuyerService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Buyers;

public interface IBuyerService
{
    /// <summary>
    /// Returns the buyer the user owns (where they are marked as Default Buyer),
    /// or null when the user owns none. Being listed as an authorized user is not
    /// enough — that only grants editing rights over someone else's orders.
    /// </summary>
    Task<Buyer?> ResolveDefaultBuyerAsync(
        string company,
        string username,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
```

- [ ] **Step 5: Implementar `BuyerService`**

Crear `src/OCAutomatica.Api/Buyers/BuyerService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Buyers;

public sealed class BuyerService : IBuyerService
{
    private const string BuyersPath = "Erp.BO.PurAgentSvc/PurAgents?$expand=PurAuths";

    private readonly IEpicorClient _epicor;

    public BuyerService(IEpicorClient epicor) => _epicor = epicor;

    public async Task<Buyer?> ResolveDefaultBuyerAsync(
        string company,
        string username,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var response = await _epicor.GetAsync<BuyerListResponse>(
            company, BuyersPath, credentials, ct);

        if (response is null) return null;

        var match = response.Value.FirstOrDefault(buyer =>
            buyer.PurAuths.Any(auth =>
                auth.IsPrimaryUser &&
                string.Equals(auth.DcdUserID, username, StringComparison.OrdinalIgnoreCase)));

        return match is null ? null : new Buyer(match.BuyerID, match.Name);
    }
}
```

- [ ] **Step 6: Correr los tests para verificar que pasan**

Run: `dotnet test --filter BuyerServiceTests`
Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 7: Commit**

```bash
git add src/OCAutomatica.Api/Buyers tests/OCAutomatica.Api.Tests/Buyers
git commit -m "feat: resolve BuyerID from Buyer Maintenance instead of hardcoded fallback"
```

---

## Task 6: Endpoints de autenticación

**Files:**
- Create: `src/OCAutomatica.Api/Auth/SessionMiddleware.cs`
- Create: `src/OCAutomatica.Api/Controllers/AuthController.cs`
- Modify: `src/OCAutomatica.Api/Program.cs`
- Modify: `src/OCAutomatica.Api/appsettings.json`

**Interfaces:**
- Consumes: `IAuthService`, `ISessionStore`, `IBuyerService`
- Produces:
  - `POST /api/auth/login` — body `{ username, password, company }` → 200 con `{ username, company }`, o 401
  - `POST /api/auth/logout` → 204
  - `GET /api/auth/me` → 200 con la sesión, o 401
  - Cookie `oca_session` (`httpOnly`, `Secure`, `SameSite=Strict`)
  - `HttpContext.Items["UserSession"]` con el `UserSession` cuando hay sesión válida

- [ ] **Step 1: Escribir el middleware de sesión**

Crear `src/OCAutomatica.Api/Auth/SessionMiddleware.cs`:

```csharp
namespace OCAutomatica.Api.Auth;

public sealed class SessionMiddleware
{
    public const string CookieName = "oca_session";
    public const string ItemKey = "UserSession";

    private readonly RequestDelegate _next;

    public SessionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ISessionStore sessions)
    {
        if (context.Request.Cookies.TryGetValue(CookieName, out var sessionId))
        {
            var session = sessions.Get(sessionId);
            if (session is not null)
            {
                context.Items[ItemKey] = session;
            }
        }

        await _next(context);
    }
}
```

- [ ] **Step 2: Escribir el controlador**

Crear `src/OCAutomatica.Api/Controllers/AuthController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Buyers;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    public sealed record LoginRequest(string Username, string Password, string Company);
    public sealed record SessionResponse(
        string Username, string Company, string Plant, string? BuyerId, string? BuyerName);

    private readonly IAuthService _auth;
    private readonly ISessionStore _sessions;
    private readonly IBuyerService _buyers;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService auth,
        ISessionStore sessions,
        IBuyerService buyers,
        ILogger<AuthController> logger)
    {
        _auth = auth;
        _sessions = sessions;
        _buyers = buyers;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var credentials = new EpicorCredentials(request.Username, request.Password);

        bool accepted;
        try
        {
            accepted = await _auth.ValidateAsync(credentials, request.Company, ct);
        }
        catch (EpicorException ex)
        {
            // AuthService already turns InvalidCredentials into accepted=false,
            // so only InvalidApiKey, AccessDenied, or Other reach this catch.
            // None of them are a wrong password and must not be reported as one.
            _logger.LogError(ex,
                "Epicor error ({Reason}) while validating {Username} on {Company}",
                ex.Reason, request.Username, request.Company);

            var message = ex.Reason switch
            {
                EpicorErrorReason.InvalidApiKey =>
                    "La aplicacion no pudo autenticarse con Epicor (clave de API invalida). Avisa a sistemas.",
                EpicorErrorReason.AccessDenied =>
                    "Tu usuario de Epicor no tiene permiso para consultar proveedores. Pide que revisen tu perfil de seguridad en Epicor.",
                _ =>
                    "No se pudo contactar a Epicor. Intenta de nuevo o avisa a sistemas."
            };

            return StatusCode(503, new { message });
        }

        if (!accepted)
        {
            _logger.LogWarning("Rejected login for {Username} on {Company}",
                request.Username, request.Company);
            return Unauthorized(new { message = "Usuario o contrasena incorrectos." });
        }

        var sessionId = _sessions.Create(credentials);
        _sessions.SetContext(sessionId, request.Company, string.Empty, null);

        Response.Cookies.Append(SessionMiddleware.CookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            MaxAge = TimeSpan.FromHours(8)
        });

        _logger.LogInformation("Login succeeded for {Username} on {Company}",
            request.Username, request.Company);

        return Ok(new SessionResponse(
            request.Username, request.Company, string.Empty, null, null));
    }

    /// <summary>
    /// Anonymous: the company must be chosen before authenticating, because the
    /// Epicor URL is company-scoped.
    /// </summary>
    [HttpGet("companies")]
    public IActionResult Companies([FromServices] IOptions<EpicorOptions> options)
        => Ok(options.Value.Companies);

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        if (Request.Cookies.TryGetValue(SessionMiddleware.CookieName, out var sessionId))
        {
            _sessions.Remove(sessionId);
        }

        Response.Cookies.Delete(SessionMiddleware.CookieName);
        return NoContent();
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
        {
            return Unauthorized();
        }

        return Ok(new SessionResponse(
            session.Username, session.Company, session.Plant, session.BuyerId, null));
    }
}
```

- [ ] **Step 3: Registrar todo en `Program.cs`**

Reemplazar el contenido de `src/OCAutomatica.Api/Program.cs`:

```csharp
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Buyers;
using OCAutomatica.Api.Epicor;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDataProtection();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<EpicorOptions>(
    builder.Configuration.GetSection(EpicorOptions.SectionName));

builder.Services.AddHttpClient<IEpicorClient, EpicorClient>();

builder.Services.AddSingleton<ISessionStore, SessionStore>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBuyerService, BuyerService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseMiddleware<SessionMiddleware>();
app.MapControllers();

app.Run();
```

- [ ] **Step 4: Configurar `appsettings.json`**

Reemplazar el contenido de `src/OCAutomatica.Api/appsettings.json`:

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
    "Companies": [ "CFSJ_LAF", "CFSJ_RIO" ]
  }
}
```

**Por qué las compañías van en configuración:** la URL de Epicor incluye la compañía, así que hay que elegirla *antes* de autenticarse — no se puede consultar la lista desde Epicor sin estar dentro. La alternativa sería fijarlas en el código del frontend, y ahí se quedarían para siempre. En configuración, agregar una sucursal es editar un archivo en el servidor.

Agregar la propiedad a `EpicorOptions.cs`:

```csharp
public List<string> Companies { get; set; } = new();
```

- [ ] **Step 5: Guardar los secretos fuera del repositorio**

```bash
cd src/OCAutomatica.Api
dotnet user-secrets init
dotnet user-secrets set "Epicor:BaseUrl" "https://srvcsjpr2.carnessanjuan.local/Kinetic2026_1"
dotnet user-secrets set "Epicor:ApiKey" "TU_API_KEY"
```

- [ ] **Step 6: Verificar que compila y los tests siguen pasando**

Run: `dotnet test`
Expected: `Passed! - Failed: 0, Passed: 24`

- [ ] **Step 7: Probar el login manualmente contra el Epicor de pruebas**

```bash
dotnet run --project src/OCAutomatica.Api
```

En otra terminal, con credenciales válidas:

```bash
curl -k -i -X POST https://localhost:7000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"TU_USUARIO","password":"TU_PASS","company":"CFSJ_LAF"}'
```

Expected: HTTP 200, y un encabezado `Set-Cookie: oca_session=...; httponly; secure; samesite=strict`

Ahora con una contraseña incorrecta:

```bash
curl -k -i -X POST https://localhost:7000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"TU_USUARIO","password":"incorrecta","company":"CFSJ_LAF"}'
```

Expected: HTTP 401 con `{"message":"Usuario o contrasena incorrectos."}`

- [ ] **Step 8: Commit**

```bash
git add src/OCAutomatica.Api
git commit -m "feat: add login, logout and session endpoints with httpOnly cookie"
```

---

## Task 7: Compañías, plantas y contexto de sesión

**Files:**
- Create: `src/OCAutomatica.Api/Organization/OrganizationModels.cs`
- Create: `src/OCAutomatica.Api/Organization/IOrganizationService.cs`
- Create: `src/OCAutomatica.Api/Organization/OrganizationService.cs`
- Create: `src/OCAutomatica.Api/Controllers/OrganizationController.cs`
- Modify: `src/OCAutomatica.Api/Program.cs`
- Test: `tests/OCAutomatica.Api.Tests/Organization/OrganizationServiceTests.cs`

**Interfaces:**
- Consumes: `IEpicorClient`, `ISessionStore`, `IBuyerService`
- Produces:
  - `record Plant(string PlantId, string Name)`
  - `interface IOrganizationService { Task<IReadOnlyList<Plant>> GetPlantsAsync(string company, EpicorCredentials creds, CancellationToken ct = default) }`
  - `GET /api/organization/plants` → lista de plantas de la compañía en sesión
  - `POST /api/organization/context` — body `{ plant }` → fija planta y resuelve BuyerID

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/OCAutomatica.Api.Tests/Organization/OrganizationServiceTests.cs`:

```csharp
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Organization;

namespace OCAutomatica.Api.Tests.Organization;

public class OrganizationServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly object? _response;

        public StubEpicorClient(object? response) => _response = response;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_response);

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_response);
    }

    private static readonly EpicorCredentials Creds = new("jyanez", "x");

    [Fact]
    public async Task GetPlantsAsync_MapsEpicorResponse()
    {
        var response = new PlantListResponse
        {
            Value = new List<PlantDto>
            {
                new() { Plant1 = "MfgSys", Name = "PLANTA LA FE" },
                new() { Plant1 = "RIO", Name = "PLANTA RIO" }
            }
        };
        var service = new OrganizationService(new StubEpicorClient(response));

        var plants = await service.GetPlantsAsync("CFSJ_LAF", Creds);

        Assert.Equal(2, plants.Count);
        Assert.Equal("MfgSys", plants[0].PlantId);
        Assert.Equal("PLANTA LA FE", plants[0].Name);
    }

    [Fact]
    public async Task GetPlantsAsync_ReturnsEmptyWhenNoResponse()
    {
        var service = new OrganizationService(new StubEpicorClient(null));

        var plants = await service.GetPlantsAsync("CFSJ_LAF", Creds);

        Assert.Empty(plants);
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter OrganizationServiceTests`
Expected: FAIL — `OrganizationService` no existe

- [ ] **Step 3: Implementar modelos y servicio**

Crear `src/OCAutomatica.Api/Organization/OrganizationModels.cs`:

```csharp
namespace OCAutomatica.Api.Organization;

public sealed record Plant(string PlantId, string Name);

public sealed class PlantListResponse
{
    public List<PlantDto> Value { get; set; } = new();
}

public sealed class PlantDto
{
    /// <summary>Epicor exposes the Plant key as "Plant1" in the OData payload.</summary>
    public string Plant1 { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
```

Crear `src/OCAutomatica.Api/Organization/IOrganizationService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Organization;

public interface IOrganizationService
{
    Task<IReadOnlyList<Plant>> GetPlantsAsync(
        string company,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
```

Crear `src/OCAutomatica.Api/Organization/OrganizationService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Organization;

public sealed class OrganizationService : IOrganizationService
{
    private const string PlantsPath = "Erp.BO.PlantSvc/Plants?$select=Plant1,Name";

    private readonly IEpicorClient _epicor;

    public OrganizationService(IEpicorClient epicor) => _epicor = epicor;

    public async Task<IReadOnlyList<Plant>> GetPlantsAsync(
        string company,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var response = await _epicor.GetAsync<PlantListResponse>(
            company, PlantsPath, credentials, ct);

        if (response is null) return Array.Empty<Plant>();

        return response.Value
            .Select(p => new Plant(p.Plant1, p.Name))
            .ToList();
    }
}
```

- [ ] **Step 4: Correr los tests para verificar que pasan**

Run: `dotnet test --filter OrganizationServiceTests`
Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 5: Escribir el controlador**

Crear `src/OCAutomatica.Api/Controllers/OrganizationController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Buyers;
using OCAutomatica.Api.Organization;

namespace OCAutomatica.Api.Controllers;

[ApiController]
[Route("api/organization")]
public sealed class OrganizationController : ControllerBase
{
    public sealed record SetContextRequest(string Plant);
    public sealed record ContextResponse(
        string Company, string Plant, string? BuyerId, string? BuyerName, bool CanCreateOrders);

    private readonly IOrganizationService _organization;
    private readonly IBuyerService _buyers;
    private readonly ISessionStore _sessions;
    private readonly ILogger<OrganizationController> _logger;

    public OrganizationController(
        IOrganizationService organization,
        IBuyerService buyers,
        ISessionStore sessions,
        ILogger<OrganizationController> logger)
    {
        _organization = organization;
        _buyers = buyers;
        _sessions = sessions;
        _logger = logger;
    }

    [HttpGet("plants")]
    public async Task<IActionResult> GetPlants(CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        var plants = await _organization.GetPlantsAsync(session.Company, credentials, ct);
        return Ok(plants);
    }

    [HttpPost("context")]
    public async Task<IActionResult> SetContext(SetContextRequest request, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        var buyer = await _buyers.ResolveDefaultBuyerAsync(
            session.Company, session.Username, credentials, ct);

        _sessions.SetContext(session.SessionId, session.Company, request.Plant, buyer?.BuyerId);

        if (buyer is null)
        {
            _logger.LogWarning(
                "User {Username} has no default buyer on {Company}; order creation blocked",
                session.Username, session.Company);
        }

        return Ok(new ContextResponse(
            session.Company,
            request.Plant,
            buyer?.BuyerId,
            buyer?.Name,
            CanCreateOrders: buyer is not null));
    }
}
```

- [ ] **Step 6: Registrar el servicio en `Program.cs`**

En `src/OCAutomatica.Api/Program.cs`, agregar el `using` y el registro:

```csharp
using OCAutomatica.Api.Organization;
```

Y junto a los demás registros de servicios:

```csharp
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
```

- [ ] **Step 7: Verificar que todo compila y pasa**

Run: `dotnet test`
Expected: `Passed! - Failed: 0, Passed: 26`

- [ ] **Step 8: Commit**

```bash
git add src/OCAutomatica.Api tests/OCAutomatica.Api.Tests/Organization
git commit -m "feat: add plant listing and session context with buyer resolution"
```

---

## Task 8: Frontend — login y selección de contexto

**Files:**
- Create: `src/web/package.json`
- Create: `src/web/vite.config.ts`
- Create: `src/web/tsconfig.json`
- Create: `src/web/index.html`
- Create: `src/web/src/main.tsx`
- Create: `src/web/src/App.tsx`
- Create: `src/web/src/api/client.ts`
- Create: `src/web/src/auth/useSession.ts`
- Create: `src/web/src/auth/LoginPage.tsx`
- Create: `src/web/src/auth/ContextPicker.tsx`

**Interfaces:**
- Consumes: `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/logout`, `GET /api/organization/plants`, `POST /api/organization/context`
- Produces: aplicación React que autentica y fija compañía, planta y comprador

- [ ] **Step 1: Crear el proyecto React**

```bash
cd "c:/Users/giovanni.montoya/Proyectos Programacion/OCAutomatica/src"
npm create vite@latest web -- --template react-ts
cd web
npm install
```

- [ ] **Step 2: Configurar el proxy hacia la API**

Reemplazar `src/web/vite.config.ts`:

```typescript
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'https://localhost:7000',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
```

- [ ] **Step 3: Escribir el cliente de API**

Crear `src/web/src/api/client.ts`:

```typescript
export interface Session {
  username: string
  company: string
  plant: string
  buyerId: string | null
  buyerName: string | null
}

export interface Plant {
  plantId: string
  name: string
}

export interface Context {
  company: string
  plant: string
  buyerId: string | null
  buyerName: string | null
  canCreateOrders: boolean
}

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message)
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })

  if (!response.ok) {
    let message = 'Ocurrio un error inesperado.'
    try {
      const body = await response.json()
      if (body?.message) message = body.message
    } catch {
      // La respuesta no traia JSON; se usa el mensaje generico.
    }
    throw new ApiError(response.status, message)
  }

  return response.status === 204 ? (undefined as T) : response.json()
}

export const api = {
  companies: () => request<string[]>('/api/auth/companies'),

  login: (username: string, password: string, company: string) =>
    request<Session>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password, company }),
    }),

  logout: () => request<void>('/api/auth/logout', { method: 'POST' }),

  me: () => request<Session>('/api/auth/me'),

  plants: () => request<Plant[]>('/api/organization/plants'),

  setContext: (plant: string) =>
    request<Context>('/api/organization/context', {
      method: 'POST',
      body: JSON.stringify({ plant }),
    }),
}
```

- [ ] **Step 4: Escribir el hook de sesión**

Crear `src/web/src/auth/useSession.ts`:

```typescript
import { useCallback, useEffect, useState } from 'react'
import { api, type Session } from '../api/client'

export function useSession() {
  const [session, setSession] = useState<Session | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    api
      .me()
      .then(setSession)
      .catch(() => setSession(null))
      .finally(() => setLoading(false))
  }, [])

  const signOut = useCallback(async () => {
    await api.logout()
    setSession(null)
  }, [])

  return { session, setSession, loading, signOut }
}
```

- [ ] **Step 5: Escribir la pantalla de login**

Crear `src/web/src/auth/LoginPage.tsx`:

```tsx
import { useEffect, useState, type FormEvent } from 'react'
import { api, type Session } from '../api/client'

interface Props {
  onSignedIn: (session: Session) => void
}

export function LoginPage({ onSignedIn }: Props) {
  const [companies, setCompanies] = useState<string[]>([])
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [company, setCompany] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api
      .companies()
      .then((list) => {
        setCompanies(list)
        if (list.length > 0) setCompany(list[0])
      })
      .catch(() => setError('No se pudo cargar la lista de companias.'))
  }, [])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)
    try {
      const session = await api.login(username, password, company)
      onSignedIn(session)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo iniciar sesion.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={handleSubmit}>
      <h1>OC Automatica</h1>

      <label htmlFor="company">Compania</label>
      <select
        id="company"
        value={company}
        onChange={(e) => setCompany(e.target.value)}
      >
        {companies.map((c) => (
          <option key={c} value={c}>
            {c}
          </option>
        ))}
      </select>

      <label htmlFor="username">Usuario</label>
      <input
        id="username"
        value={username}
        onChange={(e) => setUsername(e.target.value)}
        autoComplete="username"
        required
      />

      <label htmlFor="password">Contrasena</label>
      <input
        id="password"
        type="password"
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        autoComplete="current-password"
        required
      />

      {error && <p role="alert">{error}</p>}

      <button type="submit" disabled={busy || !company}>
        {busy ? 'Validando...' : 'Entrar'}
      </button>
    </form>
  )
}
```

- [ ] **Step 6: Escribir el selector de contexto**

Crear `src/web/src/auth/ContextPicker.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { api, type Context, type Plant } from '../api/client'

interface Props {
  onContextSet: (context: Context) => void
}

export function ContextPicker({ onContextSet }: Props) {
  const [plants, setPlants] = useState<Plant[]>([])
  const [selected, setSelected] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api
      .plants()
      .then((list) => {
        setPlants(list)
        if (list.length > 0) setSelected(list[0].plantId)
      })
      .catch((err) => setError(err.message))
  }, [])

  async function confirm() {
    setBusy(true)
    setError(null)
    try {
      const context = await api.setContext(selected)
      if (!context.canCreateOrders) {
        setError(
          'Tu usuario no esta asignado como comprador en esta compania. ' +
            'Puedes consultar, pero no crear ordenes. ' +
            'Pide que te agreguen en Buyer Maintenance.',
        )
      }
      onContextSet(context)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo fijar el contexto.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section>
      <h2>Selecciona la planta</h2>

      <select value={selected} onChange={(e) => setSelected(e.target.value)}>
        {plants.map((plant) => (
          <option key={plant.plantId} value={plant.plantId}>
            {plant.name}
          </option>
        ))}
      </select>

      {error && <p role="alert">{error}</p>}

      <button type="button" onClick={confirm} disabled={busy || !selected}>
        {busy ? 'Cargando...' : 'Continuar'}
      </button>
    </section>
  )
}
```

- [ ] **Step 7: Ensamblar `App.tsx`**

Reemplazar `src/web/src/App.tsx`:

```tsx
import { useState } from 'react'
import { type Context } from './api/client'
import { LoginPage } from './auth/LoginPage'
import { ContextPicker } from './auth/ContextPicker'
import { useSession } from './auth/useSession'

export default function App() {
  const { session, setSession, loading, signOut } = useSession()
  const [context, setContext] = useState<Context | null>(null)

  if (loading) return <p>Cargando...</p>

  if (!session) return <LoginPage onSignedIn={setSession} />

  if (!context) return <ContextPicker onContextSet={setContext} />

  return (
    <main>
      <header>
        <span>{session.username}</span>
        <span>
          {context.company} / {context.plant}
        </span>
        <span>{context.buyerName ?? 'Sin comprador asignado'}</span>
        <button type="button" onClick={signOut}>
          Salir
        </button>
      </header>

      <p>Sesion iniciada. El grid de productos llega en el Plan 2.</p>
    </main>
  )
}
```

- [ ] **Step 8: Verificar el flujo completo manualmente**

En una terminal:

```bash
dotnet run --project src/OCAutomatica.Api
```

En otra:

```bash
cd src/web
npm run dev
```

Abre la URL que imprime Vite y confirma, en orden:

1. Aparece la pantalla de login y el selector de compañía se llena solo desde la configuración del servidor
2. Con contraseña incorrecta se muestra "Usuario o contrasena incorrectos."
3. Con credenciales correctas pasa al selector de planta
4. Al elegir planta y continuar, el encabezado muestra usuario, compañía/planta y el nombre del comprador
5. En las DevTools, pestaña Application → Cookies, la cookie `oca_session` aparece marcada como `HttpOnly`
6. En DevTools → Network, **ninguna respuesta contiene la contraseña**
7. El botón Salir regresa al login

- [ ] **Step 9: Commit**

```bash
git add src/web
git commit -m "feat: add React login and context selection screens"
```

---

## Verificación final del Plan 1

- [ ] `dotnet test` pasa completo (26 tests)
- [ ] Un comprador puede autenticarse con sus credenciales de Epicor
- [ ] Una contraseña incorrecta devuelve 401 con mensaje claro
- [ ] Epicor caído devuelve 503 con mensaje distinto al de contraseña incorrecta
- [ ] La cookie de sesión es `httpOnly`, `Secure` y `SameSite=Strict`
- [ ] Ninguna respuesta HTTP contiene la contraseña del usuario
- [ ] El BuyerID se resuelve desde Buyer Maintenance
- [ ] Un usuario sin Default Buyer recibe un mensaje explicativo y queda marcado como no autorizado para crear órdenes
- [ ] No hay cadenas de conexión ni credenciales en el código ni en `appsettings.json`

**Siguiente:** Plan 2 — el grid de productos por proveedor.
