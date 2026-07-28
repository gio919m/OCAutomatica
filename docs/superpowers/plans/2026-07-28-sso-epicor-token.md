# OC Automática — SSO desde Epicor Kinetic vía Token Authentication

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Un usuario que ya inició sesión en Kinetic puede entrar a OCAutomatica lanzándola desde un menú Kinetic ("Web Bridge") sin volver a teclear su usuario/contraseña de Epicor. El login manual existente no cambia.

**Architecture:** Se agrega un endpoint nuevo (`POST /api/auth/sso-login`) que valida la firma HMACSHA256 de un JWT emitido por la función nativa "Token Authentication" de Epicor Kinetic (misma Sign Key configurada en su Server Management), extrae el `username`, reutiliza el `AuthService` existente para confirmar sus compañías (ahora vía Bearer en vez de Basic Auth), re-emite un JWT propio de mayor duración, y arranca la sesión exactamente igual que el login manual. El frontend detecta el token en la URL al arrancar y, si es válido, entra directo sin mostrar el formulario de login.

**Tech Stack:** .NET 8 y React 19 + TypeScript ya en uso. Sin librerías nuevas — el JWT se firma/valida a mano con `System.Security.Cryptography.HMACSHA256` (ya en el BCL), porque Epicor codifica `exp`/`iat` como *strings*, un formato que las librerías estándar de JWT no producen por defecto.

## Global Constraints

- **Epicor REST v2** únicamente para datos de Epicor. Ninguna conexión SQL directa a Epicor.
- **NO usar FluentAssertions ni librerías de mocking.** Solo asserts de xUnit y stubs/fakes escritos a mano.
- Código y comentarios en **inglés**; mensajes de UI en **español**.
- La Sign Key (`EpicorToken:SignKey`) nunca se expone al cliente, nunca se loggea, y **nunca se escribe en texto plano dentro de este repo de planes/specs** — solo vive en `appsettings.Development.json` local (mismo tratamiento que hoy tiene `Epicor:ApiKey`).
- `IssueSessionToken` solo se invoca internamente después de un `TryValidate` exitoso sobre un token ya firmado por Epicor — **nunca** a partir de un username enviado libremente por el cliente (la Sign Key firma para cualquier usuario sin verificar contraseña; esa es la única regla de confianza que no puede romperse).
- El login manual existente (`POST /api/auth/login`, `LoginPage.tsx`) no cambia su comportamiento.
- Cada tarea registra en `Program.cs` **solo** el servicio que ella misma crea, en el punto donde ese tipo pasa a existir — nunca registrar por adelantado un tipo que una tarea futura todavía no ha creado (lección de un bug real de este proyecto: como el proyecto de tests referencia todo el ensamblado de la API, una `Program.cs` que no compila rompe la suite entera, no solo lo nuevo).

---

## Contexto: interfaces ya existentes (no se repiten, solo se consumen)

- `IEpicorClient.GetAsync<T>(company, relativePath, credentials, ct)` / `PostAsync<T>(...)` — `src/OCAutomatica.Api/Epicor/EpicorClient.cs`. `AddAuthHeaders` arma hoy siempre Basic Auth desde `credentials.Username`/`Password`, más `x-api-key`, más `SessionInfo` si `credentials.EpicorSessionId` está presente.
- `EpicorCredentials(string Username, string Password)` con la propiedad opcional `EpicorSessionId` ya agregada — `src/OCAutomatica.Api/Epicor/EpicorCredentials.cs`.
- `EpicorException`, `EpicorErrorReason` — `src/OCAutomatica.Api/Epicor/`.
- `ISessionStore` (`Create`, `Get`, `GetCredentials`, `SetAvailableCompanies`, `SetEpicorSessionId`, `SetContext`, `Remove`) y su implementación `SessionStore` — `src/OCAutomatica.Api/Auth/`. Hoy `SessionStore` cifra únicamente la password con `IDataProtector`.
- `IAuthService.ValidateAsync(EpicorCredentials credentials, CancellationToken ct = default)` → `Task<IReadOnlyList<CompanyAccess>?>` (`null` = credenciales inválidas, lista vacía = usuario sin compañías) — `src/OCAutomatica.Api/Auth/AuthService.cs`. Depende únicamente de `IEpicorClient`, es agnóstico al tipo de credencial — **no necesita cambios**.
- `UserSession { string SessionId; string Username; string Company; string Plant; string? BuyerId; string? EpicorSessionId; IReadOnlyList<CompanyAccess> AvailableCompanies; DateTimeOffset LastSeenUtc; }`, `CompanyAccess(string Company, string CompanyName)` — `src/OCAutomatica.Api/Auth/UserSession.cs`.
- `AuthController` (`src/OCAutomatica.Api/Controllers/AuthController.cs`) ya tiene `_auth`, `_sessions`, `_epicor`, `_logger` inyectados, el método privado `EstablishEpicorSessionAsync(string sessionId, string company, EpicorCredentials credentials, CancellationToken ct)` (llama a `Ice.Lib.SessionModSvc/Login`, best-effort) y los records `LoginRequest`, `SessionResponse`, `CompanyOption`.
- `OrganizationController.SetContext` (`POST /api/organization/context`, ya existente, **no se toca**) resuelve el `BuyerId` a partir de un `Plant` y fija `session.Plant` — este plan reutiliza ese endpoint desde el frontend, no duplica su lógica.
- Frontend: `src/web/src/api/client.ts` exporta `api.login`, `api.selectCompany`, `api.logout`, `api.me`, `api.setContext`, tipos `Session { username, company, plant, buyerId, buyerName, companies }` y `Context { company, plant, buyerId, buyerName, canCreateOrders }`.
- `src/web/src/auth/useSession.ts` — hook que hoy solo llama `api.me()` al montar y expone `{ session, setSession, loading, signOut }`.
- `src/web/src/App.tsx` — dueño del estado `context` (`useState<Context | null>`), renderiza `<LoginPage>` si `!session`, `<CompanyPicker>` si `!session.company`, `<ContextPicker>` si `!context`.
- Tests: `tests/OCAutomatica.Api.Tests/Epicor/EpicorClientTests.cs`, `tests/OCAutomatica.Api.Tests/Auth/SessionStoreTests.cs`, `tests/OCAutomatica.Api.Tests/Integration/AuthEndpointsTests.cs` (usa `TestWebApplicationFactory` + `TestEpicorClient` con `OnGet`/`OnPost` configurables), `tests/OCAutomatica.Api.Tests/Fakes/FakeHttpMessageHandler.cs`.

---

## Estructura de archivos

```
src/OCAutomatica.Api/
├── Epicor/
│   ├── EpicorTokenOptions.cs        # (nuevo)
│   ├── IEpicorTokenService.cs       # (nuevo)
│   ├── EpicorTokenService.cs        # (nuevo)
│   ├── EpicorCredentials.cs         # (modificar: + BearerToken)
│   └── EpicorClient.cs              # (modificar: AddAuthHeaders soporta Bearer)
├── Auth/
│   ├── ISessionStore.cs             # (sin cambios de firma)
│   └── SessionStore.cs              # (modificar: guarda password O bearer token)
├── Controllers/
│   └── AuthController.cs            # (modificar: + SsoLogin)
├── Program.cs                       # (modificar: registrar EpicorTokenOptions + IEpicorTokenService — Task 1 únicamente)
├── appsettings.json                 # (modificar: + sección EpicorToken vacía)
└── appsettings.Development.json     # (modificar localmente: + Sign Key real — ver nota de seguridad)

tests/OCAutomatica.Api.Tests/
├── Epicor/
│   ├── EpicorTokenServiceTests.cs   # (nuevo)
│   └── EpicorClientTests.cs         # (modificar: + tests de Bearer)
├── Auth/
│   └── SessionStoreTests.cs         # (modificar: + tests de Bearer)
└── Integration/
    ├── TestWebApplicationFactory.cs # (modificar: configura una Sign Key de prueba)
    └── AuthEndpointsTests.cs        # (modificar: + tests de sso-login)

src/web/src/
├── api/client.ts                    # (modificar: + ssoLogin)
├── auth/useSession.ts               # (modificar: detecta token en la URL)
└── App.tsx                          # (modificar: auto-selecciona planta si vino de SSO)
```

---

## Task 1: `EpicorTokenOptions` + `IEpicorTokenService` — validar y re-emitir el JWT

**Files:**
- Create: `src/OCAutomatica.Api/Epicor/EpicorTokenOptions.cs`
- Create: `src/OCAutomatica.Api/Epicor/IEpicorTokenService.cs`
- Create: `src/OCAutomatica.Api/Epicor/EpicorTokenService.cs`
- Modify: `src/OCAutomatica.Api/Program.cs`
- Modify: `src/OCAutomatica.Api/appsettings.json`
- Test: `tests/OCAutomatica.Api.Tests/Epicor/EpicorTokenServiceTests.cs`

**Por qué existe esta tarea:** es la pieza criptográfica aislada — validar un JWT firmado por Epicor y poder emitir uno propio en el mismo formato exacto (`exp`/`iat` como *strings*, `iss`/`aud` = `"epicor"`), confirmado en vivo contra la REST API real (spec, sección "Contexto confirmado en vivo"). No depende de nada más de este plan; todo lo demás depende de ella.

**Interfaces:**
- Produces: `EpicorTokenOptions { string SignKey; int SessionLifetimeSeconds; }`. `IEpicorTokenService.TryValidate(string token, out string username)` → `bool`. `IEpicorTokenService.IssueSessionToken(string username)` → `string`. Las Tasks 2-4 consumen `IEpicorTokenService`.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/OCAutomatica.Api.Tests/Epicor/EpicorTokenServiceTests.cs`:

```csharp
using System.Text;
using Microsoft.Extensions.Options;
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Tests.Epicor;

public class EpicorTokenServiceTests
{
    // Arbitrary Base64 test value — never the real Epicor Sign Key.
    private const string TestSignKey = "dGVzdC1zaWduLWtleS1mb3ItdW5pdC10ZXN0cw==";
    private const string OtherSignKey = "YW5vdGhlci1jb21wbGV0ZWx5LWRpZmZlcmVudC1rZXk=";

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    private static EpicorTokenService BuildService(
        string signKey = TestSignKey, int lifetimeSeconds = 28800, DateTimeOffset? at = null) =>
        new(
            Options.Create(new EpicorTokenOptions { SignKey = signKey, SessionLifetimeSeconds = lifetimeSeconds }),
            new FixedTimeProvider(at ?? Now));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Builds a raw JWT independently of EpicorTokenService, so tests
    /// never validate the implementation against itself.</summary>
    private static string BuildRawToken(
        string signKey, string iss, string aud, string username, long iat, long exp)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var payloadJson =
            $$"""{"exp":"{{exp}}","iat":"{{iat}}","iss":"{{iss}}","aud":"{{aud}}","username":"{{username}}"}""";
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = Encoding.UTF8.GetBytes($"{header}.{payload}");
        using var hmac = new System.Security.Cryptography.HMACSHA256(Convert.FromBase64String(signKey));
        var signature = Base64UrlEncode(hmac.ComputeHash(signingInput));
        return $"{header}.{payload}.{signature}";
    }

    [Fact]
    public void IssueSessionToken_ProducesATokenTryValidateAccepts()
    {
        var service = BuildService();

        var token = service.IssueSessionToken("epicor");

        Assert.True(service.TryValidate(token, out var username));
        Assert.Equal("epicor", username);
    }

    [Fact]
    public void TryValidate_RejectsATokenSignedWithADifferentKey()
    {
        var issuer = BuildService(signKey: OtherSignKey);
        var validator = BuildService(signKey: TestSignKey);

        var token = issuer.IssueSessionToken("epicor");

        Assert.False(validator.TryValidate(token, out _));
    }

    [Fact]
    public void TryValidate_RejectsAnExpiredToken()
    {
        var issuer = BuildService(lifetimeSeconds: 3600, at: Now);
        var token = issuer.IssueSessionToken("epicor");

        var validator = BuildService(at: Now.AddSeconds(3601));

        Assert.False(validator.TryValidate(token, out _));
    }

    [Fact]
    public void TryValidate_AcceptsATokenAtTheExactExpirationBoundaryMinusOneSecond()
    {
        var issuer = BuildService(lifetimeSeconds: 3600, at: Now);
        var token = issuer.IssueSessionToken("epicor");

        var validator = BuildService(at: Now.AddSeconds(3599));

        Assert.True(validator.TryValidate(token, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    [InlineData("a.b.c")]
    public void TryValidate_RejectsAMalformedToken(string malformed)
    {
        var service = BuildService();

        Assert.False(service.TryValidate(malformed, out var username));
        Assert.Equal(string.Empty, username);
    }

    [Fact]
    public void TryValidate_RejectsAWellSignedTokenWithTheWrongIssuer()
    {
        var service = BuildService();
        var now = Now.ToUnixTimeSeconds();
        var forged = BuildRawToken(TestSignKey, iss: "not-epicor", aud: "epicor", username: "epicor",
            iat: now, exp: now + 3600);

        Assert.False(service.TryValidate(forged, out _));
    }

    [Fact]
    public void TryValidate_RejectsAWellSignedTokenWithTheWrongAudience()
    {
        var service = BuildService();
        var now = Now.ToUnixTimeSeconds();
        var forged = BuildRawToken(TestSignKey, iss: "epicor", aud: "not-epicor", username: "epicor",
            iat: now, exp: now + 3600);

        Assert.False(service.TryValidate(forged, out _));
    }

    [Fact]
    public void TryValidate_RejectsATokenWhoseSignatureWasTamperedWith()
    {
        var service = BuildService();
        var token = service.IssueSessionToken("epicor");
        var parts = token.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{parts[2][..^2]}xx";

        Assert.False(service.TryValidate(tampered, out _));
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter EpicorTokenServiceTests`
Expected: FAIL — `EpicorTokenOptions`, `EpicorTokenService` no existen todavía.

- [ ] **Step 3: Crear `EpicorTokenOptions`**

Crear `src/OCAutomatica.Api/Epicor/EpicorTokenOptions.cs`:

```csharp
namespace OCAutomatica.Api.Epicor;

/// <summary>
/// Settings for validating/re-issuing JWTs from Epicor Kinetic's native
/// "Token Authentication" feature (Server Management → Application Server →
/// Configure Token Authentication).
/// </summary>
public sealed class EpicorTokenOptions
{
    public const string SectionName = "EpicorToken";

    /// <summary>Sign Key shown in Epicor's Token Authentication settings (Base64, standard — not URL-safe).</summary>
    public string SignKey { get; set; } = string.Empty;

    /// <summary>Lifetime (seconds) of tokens this app re-issues. Should match the session cookie's MaxAge (8h = 28800).</summary>
    public int SessionLifetimeSeconds { get; set; } = 28800;
}
```

- [ ] **Step 4: Crear `IEpicorTokenService`**

Crear `src/OCAutomatica.Api/Epicor/IEpicorTokenService.cs`:

```csharp
namespace OCAutomatica.Api.Epicor;

public interface IEpicorTokenService
{
    /// <summary>
    /// Validates signature (HMACSHA256 with the shared Sign Key), issuer/audience
    /// ("epicor"), and expiration of a JWT issued by Epicor's own Token
    /// Authentication feature. Never throws for a malformed token — it is
    /// untrusted input from the client.
    /// </summary>
    bool TryValidate(string token, out string username);

    /// <summary>
    /// Mints a new token in the exact same format, signed with the same Sign
    /// Key, for <paramref name="username"/>. Callers must only ever pass a
    /// username that came from a successful TryValidate call on an
    /// Epicor-issued token — never a client-supplied value, since the Sign
    /// Key can mint a valid token for any username with no password check.
    /// </summary>
    string IssueSessionToken(string username);
}
```

- [ ] **Step 5: Implementar `EpicorTokenService`**

Crear `src/OCAutomatica.Api/Epicor/EpicorTokenService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace OCAutomatica.Api.Epicor;

public sealed class EpicorTokenService : IEpicorTokenService
{
    private const string ExpectedIssuer = "epicor";

    private readonly EpicorTokenOptions _options;
    private readonly TimeProvider _time;

    public EpicorTokenService(IOptions<EpicorTokenOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public bool TryValidate(string token, out string username)
    {
        username = string.Empty;

        var parts = token.Split('.');
        if (parts.Length != 3) return false;

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = Base64UrlDecode(parts[1]);
            signature = Base64UrlDecode(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var signingInput = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        byte[] expectedSignature;
        try
        {
            expectedSignature = ComputeSignature(signingInput);
        }
        catch (FormatException)
        {
            // SignKey itself isn't valid Base64 (e.g. misconfigured appsettings).
            return false;
        }

        if (signature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(signature, expectedSignature))
        {
            return false;
        }

        JwtPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<JwtPayload>(payloadBytes);
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload is null) return false;
        if (payload.Iss != ExpectedIssuer || payload.Aud != ExpectedIssuer) return false;
        if (string.IsNullOrWhiteSpace(payload.Username)) return false;
        if (!long.TryParse(payload.Exp, out var exp)) return false;
        if (_time.GetUtcNow().ToUnixTimeSeconds() >= exp) return false;

        username = payload.Username;
        return true;
    }

    public string IssueSessionToken(string username)
    {
        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        var exp = now + _options.SessionLifetimeSeconds;

        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new JwtPayload
        {
            Exp = exp.ToString(),
            Iat = now.ToString(),
            Iss = ExpectedIssuer,
            Aud = ExpectedIssuer,
            Username = username
        }));

        var signingInput = Encoding.UTF8.GetBytes($"{header}.{payload}");
        var signature = Base64UrlEncode(ComputeSignature(signingInput));

        return $"{header}.{payload}.{signature}";
    }

    private byte[] ComputeSignature(byte[] input)
    {
        using var hmac = new HMACSHA256(Convert.FromBase64String(_options.SignKey));
        return hmac.ComputeHash(input);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty
        };
        return Convert.FromBase64String(padded);
    }

    // exp/iat are strings (not numbers) in Epicor's own tokens — confirmed
    // live by decoding a real token issued by Kinetic's Token Authentication.
    private sealed class JwtPayload
    {
        [JsonPropertyName("exp")] public string Exp { get; set; } = string.Empty;
        [JsonPropertyName("iat")] public string Iat { get; set; } = string.Empty;
        [JsonPropertyName("iss")] public string Iss { get; set; } = string.Empty;
        [JsonPropertyName("aud")] public string Aud { get; set; } = string.Empty;
        [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    }
}
```

- [ ] **Step 6: Registrar en `Program.cs`**

En `src/OCAutomatica.Api/Program.cs`, agregar junto a las demás `Configure<...>` (después de `EmailQueueOptions`):

```csharp
builder.Services.Configure<EpicorTokenOptions>(
    builder.Configuration.GetSection(EpicorTokenOptions.SectionName));
```

Y junto a los demás `AddSingleton`/`AddScoped` (después de `IEmailQueueRepository`, antes de `IPurchaseOrderReportService` está bien — el orden entre servicios no relacionados no importa):

```csharp
builder.Services.AddSingleton<IEpicorTokenService, EpicorTokenService>();
```

- [ ] **Step 7: Agregar la sección vacía a `appsettings.json`**

En `src/OCAutomatica.Api/appsettings.json`, agregar junto a `"Epicor"`:

```json
  "EpicorToken": {
    "SignKey": "",
    "SessionLifetimeSeconds": 28800
  },
```

**Nota de seguridad — no es un paso automatizable:** en tu `appsettings.Development.json` local, agrega la misma sección `EpicorToken` con la Sign Key **real** que ya viste en Epicor Server Management → Application Server → Configure Token Authentication. No escribas ese valor real en ningún archivo de este repo de specs/plans ni lo pegues en mensajes de commit.

- [ ] **Step 8: Correr los tests para verificar que pasan**

Run: `dotnet test --filter EpicorTokenServiceTests`
Expected: `Passed! - Failed: 0, Passed: 12`

- [ ] **Step 9: Correr toda la suite**

Run: `dotnet test`
Expected: todos los tests existentes siguen pasando, más los 12 nuevos.

- [ ] **Step 10: Commit**

```bash
git add src/OCAutomatica.Api/Epicor/EpicorTokenOptions.cs src/OCAutomatica.Api/Epicor/IEpicorTokenService.cs src/OCAutomatica.Api/Epicor/EpicorTokenService.cs src/OCAutomatica.Api/Program.cs src/OCAutomatica.Api/appsettings.json tests/OCAutomatica.Api.Tests/Epicor/EpicorTokenServiceTests.cs
git commit -m "feat: add EpicorTokenService to validate and re-issue Epicor Kinetic SSO tokens"
```

---

## Task 2: Soporte Bearer en `EpicorCredentials`/`EpicorClient`

**Files:**
- Modify: `src/OCAutomatica.Api/Epicor/EpicorCredentials.cs`
- Modify: `src/OCAutomatica.Api/Epicor/EpicorClient.cs`
- Test: `tests/OCAutomatica.Api.Tests/Epicor/EpicorClientTests.cs`

**Por qué existe esta tarea:** hoy `EpicorClient` solo sabe armar Basic Auth. Esta tarea le agrega la capacidad de mandar `Authorization: Bearer <token>` cuando la credencial lo trae, sin romper el camino Basic Auth existente (regresión cubierta por los tests ya existentes en este mismo archivo).

**Interfaces:**
- Consumes: nada nuevo de Task 1 (independiente — trabaja sobre el tipo de credencial, no sobre cómo se validó el token).
- Produces: `EpicorCredentials { ... string? BearerToken { get; init; } }`. La Task 3 y la Task 4 lo consumen.

- [ ] **Step 1: Escribir los tests que fallan**

Agregar a `tests/OCAutomatica.Api.Tests/Epicor/EpicorClientTests.cs`, dentro de la clase existente (reutiliza `BuildClient`, `Company`, `ODataList<T>` ya declarados ahí):

```csharp
    [Fact]
    public async Task GetAsync_SendsBearerAuth_InsteadOfBasic_WhenBearerTokenIsSet()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, """{"Value":[]}""");
        var client = BuildClient(handler);

        await client.GetAsync<ODataList<Company>>(
            "CFSJ_LAF",
            "Erp.BO.CompanySvc/Companies",
            new EpicorCredentials("epicor", string.Empty) { BearerToken = "header.payload.signature" });

        var request = handler.LastRequest!;
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("header.payload.signature", request.Headers.Authorization.Parameter);
        Assert.Equal("test-api-key", request.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task GetAsync_StillSendsBasicAuth_WhenBearerTokenIsNotSet()
    {
        // Regression guard: the manual-login path (no BearerToken) must
        // behave exactly as before this task.
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
    }
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter EpicorClientTests`
Expected: FAIL — `EpicorCredentials` no tiene `BearerToken` todavía, y el request siempre manda Basic Auth.

- [ ] **Step 3: Agregar `BearerToken` a `EpicorCredentials`**

En `src/OCAutomatica.Api/Epicor/EpicorCredentials.cs`, agregar junto a `EpicorSessionId`:

```csharp
    /// <summary>
    /// When set, AddAuthHeaders sends this as "Authorization: Bearer &lt;token&gt;"
    /// instead of building Basic Auth from Username/Password. Set only for
    /// sessions that started via Epicor Kinetic SSO — the manual-login path
    /// never sets it.
    /// </summary>
    public string? BearerToken { get; init; }
```

- [ ] **Step 4: Ramificar `AddAuthHeaders`**

En `src/OCAutomatica.Api/Epicor/EpicorClient.cs`, reemplazar el cuerpo de `AddAuthHeaders`:

```csharp
    private void AddAuthHeaders(HttpRequestMessage request, EpicorCredentials credentials)
    {
        if (!string.IsNullOrEmpty(credentials.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.BearerToken);
        }
        else
        {
            var token = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        request.Headers.Add("x-api-key", _options.ApiKey);

        if (!string.IsNullOrEmpty(credentials.EpicorSessionId))
        {
            var sessionInfo = JsonSerializer.Serialize(new { SessionID = credentials.EpicorSessionId });
            request.Headers.Add("SessionInfo", sessionInfo);
        }
    }
```

- [ ] **Step 5: Correr los tests para verificar que pasan**

Run: `dotnet test --filter EpicorClientTests`
Expected: `Passed! - Failed: 0` (todos los existentes + 2 nuevos).

- [ ] **Step 6: Correr toda la suite**

Run: `dotnet test`
Expected: todos pasan.

- [ ] **Step 7: Commit**

```bash
git add src/OCAutomatica.Api/Epicor/EpicorCredentials.cs src/OCAutomatica.Api/Epicor/EpicorClient.cs tests/OCAutomatica.Api.Tests/Epicor/EpicorClientTests.cs
git commit -m "feat: support Bearer token authentication in EpicorClient"
```

---

## Task 3: Soporte Bearer en `SessionStore`

**Files:**
- Modify: `src/OCAutomatica.Api/Auth/SessionStore.cs`
- Test: `tests/OCAutomatica.Api.Tests/Auth/SessionStoreTests.cs`

**Por qué existe esta tarea:** `SessionStore` hoy solo sabe cifrar y devolver una password. Una sesión SSO necesita guardar en su lugar el JWT re-emitido, y `GetCredentials` debe reconstruir la credencial correcta (Bearer o password) según cómo se creó la sesión — sin tocar la interfaz pública `ISessionStore` (mismas firmas, `Create` ya recibe un `EpicorCredentials` completo).

**Interfaces:**
- Consumes: `EpicorCredentials.BearerToken` (Task 2).
- Produces: comportamiento — `Create`/`GetCredentials` ya soportan ambos tipos de credencial. La Task 4 lo consume al crear sesiones SSO.

- [ ] **Step 1: Escribir los tests que fallan**

Agregar a `tests/OCAutomatica.Api.Tests/Auth/SessionStoreTests.cs`, dentro de la clase existente (reutiliza `BuildStore`):

```csharp
    [Fact]
    public void GetCredentials_RoundTripsBearerToken_InsteadOfPassword()
    {
        var store = BuildStore();
        var sessionId = store.Create(
            new EpicorCredentials("epicor", string.Empty) { BearerToken = "header.payload.signature" });

        var credentials = store.GetCredentials(sessionId);

        Assert.NotNull(credentials);
        Assert.Equal("epicor", credentials!.Username);
        Assert.Equal("header.payload.signature", credentials.BearerToken);
        Assert.Equal(string.Empty, credentials.Password);
    }

    [Fact]
    public void GetCredentials_StillRoundTripsPassword_ForANonSsoSession()
    {
        // Regression guard: sessions created from the manual login path
        // (no BearerToken) must behave exactly as before this task.
        var store = BuildStore();
        var sessionId = store.Create(new EpicorCredentials("jyanez", "secreto"));

        var credentials = store.GetCredentials(sessionId);

        Assert.Equal("secreto", credentials!.Password);
        Assert.Null(credentials.BearerToken);
    }
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter SessionStoreTests`
Expected: FAIL — `GetCredentials_RoundTripsBearerToken_InsteadOfPassword` falla porque hoy siempre reconstruye con `Password`, nunca con `BearerToken`.

- [ ] **Step 3: Modificar `SessionStore`**

En `src/OCAutomatica.Api/Auth/SessionStore.cs`, reemplazar el `Entry` privado y los métodos `Create`/`GetCredentials`:

```csharp
    private sealed record Entry(UserSession Session, string ProtectedSecret, bool IsBearer);

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

        var isBearer = !string.IsNullOrEmpty(credentials.BearerToken);
        var secret = isBearer ? credentials.BearerToken! : credentials.Password;

        _entries[sessionId] = new Entry(session, _protector.Protect(secret), isBearer);
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

        var secret = _protector.Unprotect(entry.ProtectedSecret);
        return entry.IsBearer
            ? new EpicorCredentials(entry.Session.Username, string.Empty) { BearerToken = secret }
            : new EpicorCredentials(entry.Session.Username, secret);
    }
```

(El resto del archivo — `SetAvailableCompanies`, `SetEpicorSessionId`, `SetContext`, `Remove` — no cambia.)

- [ ] **Step 4: Correr los tests para verificar que pasan**

Run: `dotnet test --filter SessionStoreTests`
Expected: `Passed! - Failed: 0` (todos los existentes + 2 nuevos).

- [ ] **Step 5: Correr toda la suite**

Run: `dotnet test`
Expected: todos pasan.

- [ ] **Step 6: Commit**

```bash
git add src/OCAutomatica.Api/Auth/SessionStore.cs tests/OCAutomatica.Api.Tests/Auth/SessionStoreTests.cs
git commit -m "feat: support storing a Bearer token instead of a password in SessionStore"
```

---

## Task 4: Endpoint `POST /api/auth/sso-login`

**Files:**
- Modify: `src/OCAutomatica.Api/Controllers/AuthController.cs`
- Modify: `tests/OCAutomatica.Api.Tests/Integration/TestWebApplicationFactory.cs`
- Modify: `tests/OCAutomatica.Api.Tests/Integration/AuthEndpointsTests.cs`

**Por qué existe esta tarea:** conecta las Tasks 1-3 en el endpoint que el frontend va a llamar. Valida el token entrante, confirma las compañías del usuario vía Bearer, re-emite un token de 8 horas para el resto de la sesión, y arranca la sesión exactamente igual que `Login` (misma cookie, mismo `SessionResponse`, mismo `EstablishEpicorSessionAsync`).

**Interfaces:**
- Consumes: `IEpicorTokenService.TryValidate`/`IssueSessionToken` (Task 1), `EpicorCredentials.BearerToken` (Task 2), `ISessionStore.Create`/`SetAvailableCompanies`/`SetContext` con soporte Bearer (Task 3), `IAuthService.ValidateAsync` (ya existente, sin cambios), `EstablishEpicorSessionAsync` (ya existente en este mismo controlador, sin cambios).
- Produces: `POST /api/auth/sso-login` → mismo shape que `POST /api/auth/login` (`SessionResponse`). El Task 5 (frontend) lo consume.

- [ ] **Step 1: Escribir los tests que fallan**

Primero, en `tests/OCAutomatica.Api.Tests/Integration/TestWebApplicationFactory.cs`, agregar la configuración de una Sign Key de prueba (agregar el `using` y modificar `ConfigureWebHost`):

```csharp
using OCAutomatica.Api.Epicor;
// ... (el using de OCAutomatica.Api.Epicor ya existe en este archivo)

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    // Arbitrary Base64 test value — never the real Epicor Sign Key.
    public const string SsoSignKey = "dGVzdC1zc28tc2lnbi1rZXktZm9yLWludGVncmF0aW9uLXRlc3Rz";

    public TestEpicorClient EpicorClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IEpicorClient));
            services.AddSingleton<IEpicorClient>(EpicorClient);
            services.Configure<EpicorTokenOptions>(o =>
            {
                o.SignKey = SsoSignKey;
                o.SessionLifetimeSeconds = 28800;
            });
        });
    }

    public HttpClient CreateSecureClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost")
    });
}
```

Luego, agregar a `tests/OCAutomatica.Api.Tests/Integration/AuthEndpointsTests.cs` (agregar los `using System.Security.Cryptography;` y `using System.Text;` al inicio si no están, y el helper + tests dentro de la clase existente, reutilizando `LoginResponseBody`):

```csharp
    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Builds a raw JWT exactly like Epicor's Token Authentication
    /// would, independently of EpicorTokenService — so these tests exercise
    /// the endpoint against an input shaped like the real thing, not against
    /// the service's own encoder.</summary>
    private static string BuildKineticToken(
        string username, string signKey, DateTimeOffset issuedAt, int lifetimeSeconds = 3600)
    {
        var iat = issuedAt.ToUnixTimeSeconds();
        var exp = iat + lifetimeSeconds;
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var payloadJson =
            $$"""{"exp":"{{exp}}","iat":"{{iat}}","iss":"epicor","aud":"epicor","username":"{{username}}"}""";
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = Encoding.UTF8.GetBytes($"{header}.{payload}");
        using var hmac = new HMACSHA256(Convert.FromBase64String(signKey));
        var signature = Base64UrlEncode(hmac.ComputeHash(signingInput));
        return $"{header}.{payload}.{signature}";
    }

    [Fact]
    public async Task SsoLogin_ReturnsSessionAndSetsCookie_WhenTokenIsValid()
    {
        using var factory = new TestWebApplicationFactory();
        factory.EpicorClient.OnGet = (_, _, _) => new UserCompListResponse
        {
            Value = new List<UserCompDto>
            {
                new() { Company = "CFSJ_LAF", CompanyName = "Carnes Finas San Juan Laredo" }
            }
        };

        var token = BuildKineticToken("epicor", TestWebApplicationFactory.SsoSignKey, DateTimeOffset.UtcNow);

        using var client = factory.CreateSecureClient();
        var response = await client.PostAsJsonAsync("/api/auth/sso-login",
            new { token, company = "CFSJ_LAF", site = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));

        var body = await response.Content.ReadFromJsonAsync<LoginResponseBody>();
        Assert.Equal("epicor", body!.Username);
        Assert.Equal("CFSJ_LAF", body.Company);
    }

    [Fact]
    public async Task SsoLogin_ReturnsUnauthorized_WhenTokenSignatureIsInvalid()
    {
        using var factory = new TestWebApplicationFactory();
        var token = BuildKineticToken("epicor", "aW52YWxpZC1rZXktbm90LW1hdGNoaW5nLXNlcnZlcg==", DateTimeOffset.UtcNow);

        using var client = factory.CreateSecureClient();
        var response = await client.PostAsJsonAsync("/api/auth/sso-login",
            new { token, company = (string?)null, site = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SsoLogin_ReturnsUnauthorized_WhenTokenIsExpired()
    {
        using var factory = new TestWebApplicationFactory();
        var token = BuildKineticToken(
            "epicor", TestWebApplicationFactory.SsoSignKey,
            DateTimeOffset.UtcNow.AddHours(-2), lifetimeSeconds: 3600);

        using var client = factory.CreateSecureClient();
        var response = await client.PostAsJsonAsync("/api/auth/sso-login",
            new { token, company = (string?)null, site = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SsoLogin_LeavesCompanyUnset_WhenUrlCompanyIsNotAssignedToTheUser()
    {
        using var factory = new TestWebApplicationFactory();
        factory.EpicorClient.OnGet = (_, _, _) => new UserCompListResponse
        {
            Value = new List<UserCompDto>
            {
                new() { Company = "CFSJ_LAF", CompanyName = "Carnes Finas San Juan Laredo" }
            }
        };
        var token = BuildKineticToken("epicor", TestWebApplicationFactory.SsoSignKey, DateTimeOffset.UtcNow);

        using var client = factory.CreateSecureClient();
        var response = await client.PostAsJsonAsync("/api/auth/sso-login",
            new { token, company = "CFSJ_ANA", site = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponseBody>();
        Assert.Equal(string.Empty, body!.Company);
    }

    [Fact]
    public async Task SsoLogin_QueriesCompaniesUsingBearerAuth_WithTheOriginalToken()
    {
        using var factory = new TestWebApplicationFactory();
        string? capturedBearer = null;
        factory.EpicorClient.OnGet = (_, _, credentials) =>
        {
            capturedBearer = credentials.BearerToken;
            return new UserCompListResponse
            {
                Value = new List<UserCompDto> { new() { Company = "CFSJ_LAF", CompanyName = "LA FE" } }
            };
        };
        var token = BuildKineticToken("epicor", TestWebApplicationFactory.SsoSignKey, DateTimeOffset.UtcNow);

        using var client = factory.CreateSecureClient();
        await client.PostAsJsonAsync("/api/auth/sso-login",
            new { token, company = "CFSJ_LAF", site = (string?)null });

        Assert.Equal(token, capturedBearer);
    }

    [Fact]
    public async Task SsoLogin_EstablishesTheEpicorSession_WithARenewedTokenDifferentFromTheOriginal()
    {
        using var factory = new TestWebApplicationFactory();
        factory.EpicorClient.OnGet = (_, _, _) => new UserCompListResponse
        {
            Value = new List<UserCompDto> { new() { Company = "CFSJ_LAF", CompanyName = "LA FE" } }
        };
        string? renewedBearer = null;
        factory.EpicorClient.OnPost = (_, relativePath, _, credentials) =>
        {
            if (relativePath == "Ice.Lib.SessionModSvc/Login") renewedBearer = credentials.BearerToken;
            return null;
        };
        var originalToken = BuildKineticToken("epicor", TestWebApplicationFactory.SsoSignKey, DateTimeOffset.UtcNow);

        using var client = factory.CreateSecureClient();
        await client.PostAsJsonAsync("/api/auth/sso-login",
            new { token = originalToken, company = "CFSJ_LAF", site = (string?)null });

        Assert.NotNull(renewedBearer);
        Assert.NotEqual(originalToken, renewedBearer);
    }
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter AuthEndpointsTests`
Expected: FAIL con 404 — la ruta `/api/auth/sso-login` no existe todavía.

- [ ] **Step 3: Implementar la acción**

En `src/OCAutomatica.Api/Controllers/AuthController.cs`, agregar el using al inicio si falta (`OCAutomatica.Api.Epicor` ya está importado), inyectar `IEpicorTokenService` en el constructor, y agregar el record y la acción junto a `Login`:

```csharp
    public sealed record SsoLoginRequest(string Token, string? Company, string? Site);
```

Modificar el constructor:

```csharp
    private readonly IAuthService _auth;
    private readonly ISessionStore _sessions;
    private readonly IEpicorClient _epicor;
    private readonly IEpicorTokenService _tokens;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService auth,
        ISessionStore sessions,
        IEpicorClient epicor,
        IEpicorTokenService tokens,
        ILogger<AuthController> logger)
    {
        _auth = auth;
        _sessions = sessions;
        _epicor = epicor;
        _tokens = tokens;
        _logger = logger;
    }
```

Agregar la acción (después de `Login`):

```csharp
    [HttpPost("sso-login")]
    public async Task<IActionResult> SsoLogin(SsoLoginRequest request, CancellationToken ct)
    {
        if (!_tokens.TryValidate(request.Token, out var username))
        {
            return Unauthorized(new { message = "Token de Epicor invalido o expirado." });
        }

        var originalCredentials = new EpicorCredentials(username, string.Empty)
        {
            BearerToken = request.Token
        };

        IReadOnlyList<CompanyAccess>? companies;
        try
        {
            companies = await _auth.ValidateAsync(originalCredentials, ct);
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while validating SSO token for {Username}",
                ex.Reason, username);

            var message = ex.Reason switch
            {
                EpicorErrorReason.InvalidApiKey =>
                    "La aplicacion no pudo autenticarse con Epicor (clave de API invalida). Avisa a sistemas.",
                EpicorErrorReason.AccessDenied =>
                    "Tu usuario de Epicor no tiene permiso para consultar sus companias. Pide que revisen tu perfil de seguridad en Epicor.",
                _ =>
                    "No se pudo contactar a Epicor. Intenta de nuevo o avisa a sistemas."
            };

            return StatusCode(503, new { message });
        }

        if (companies is null)
        {
            _logger.LogWarning("Rejected SSO login for {Username} (Epicor did not accept the token)", username);
            return Unauthorized(new { message = "Token de Epicor invalido o expirado." });
        }

        if (companies.Count == 0)
        {
            _logger.LogWarning("User {Username} has no companies assigned in Epicor", username);
            return StatusCode(403, new
            {
                message = "Tu usuario no tiene companias asignadas en Epicor. Contacta a sistemas."
            });
        }

        var renewedToken = _tokens.IssueSessionToken(username);
        var credentials = new EpicorCredentials(username, string.Empty) { BearerToken = renewedToken };

        var sessionId = _sessions.Create(credentials);
        _sessions.SetAvailableCompanies(sessionId, companies);

        var urlCompanyIsValid = !string.IsNullOrEmpty(request.Company) &&
            companies.Any(c => c.Company == request.Company);
        var selectedCompany = urlCompanyIsValid
            ? request.Company!
            : companies.Count == 1 ? companies[0].Company : string.Empty;
        _sessions.SetContext(sessionId, selectedCompany, string.Empty, null);

        if (selectedCompany.Length > 0)
        {
            await EstablishEpicorSessionAsync(sessionId, selectedCompany, credentials, ct);
        }

        Response.Cookies.Append(SessionMiddleware.CookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            MaxAge = TimeSpan.FromHours(8)
        });

        _logger.LogInformation("SSO login succeeded for {Username} ({CompanyCount} companies)",
            username, companies.Count);

        return Ok(new SessionResponse(
            username,
            selectedCompany,
            string.Empty,
            null,
            null,
            companies.Select(c => new CompanyOption(c.Company, c.CompanyName)).ToList()));
    }
```

**Nota:** `selectedCompany` prefiere el `request.Company` de la URL sobre el auto-select de "una sola compañía" que usa `Login` — si el token trae una compañía válida para ese usuario, esa es la que se fija, sin importar cuántas compañías tenga en total.

- [ ] **Step 4: Correr los tests para verificar que pasan**

Run: `dotnet test --filter AuthEndpointsTests`
Expected: `Passed! - Failed: 0` (todos los existentes + 6 nuevos).

- [ ] **Step 5: Correr toda la suite**

Run: `dotnet test`
Expected: todos pasan.

- [ ] **Step 6: Commit**

```bash
git add src/OCAutomatica.Api/Controllers/AuthController.cs tests/OCAutomatica.Api.Tests/Integration/TestWebApplicationFactory.cs tests/OCAutomatica.Api.Tests/Integration/AuthEndpointsTests.cs
git commit -m "feat: add POST /api/auth/sso-login endpoint"
```

---

## Task 5: Frontend — detectar el token en la URL y entrar directo

**Files:**
- Modify: `src/web/src/api/client.ts`
- Modify: `src/web/src/auth/useSession.ts`
- Modify: `src/web/src/App.tsx`

**Por qué existe esta tarea:** la app hoy no tiene ningún router — esta tarea hace que el arranque de React detecte `token` (y opcionalmente `company`/`site`) en la URL, intercambie el token por una sesión, y si `site` es una planta válida, la fije automáticamente reusando el endpoint de selección de planta que ya existe — todo sin pantallas intermedias, y sin afectar el login manual si no hay `token` en la URL.

**Interfaces:**
- Consumes: `POST /api/auth/sso-login` (Task 4), `api.setContext` (ya existente, sin cambios).
- Produces: `api.ssoLogin(token, company?, site?)` → `Promise<Session>`. `useSession()` ahora también devuelve `ssoSite: string | null`.

- [ ] **Step 1: Agregar `ssoLogin` a `client.ts`**

En `src/web/src/api/client.ts`, agregar junto a `me`:

```typescript
  ssoLogin: (token: string, company?: string, site?: string) =>
    request<Session>('/api/auth/sso-login', {
      method: 'POST',
      body: JSON.stringify({ token, company: company ?? null, site: site ?? null }),
    }),
```

- [ ] **Step 2: Modificar `useSession.ts`**

Reemplazar el contenido completo de `src/web/src/auth/useSession.ts`:

```typescript
import { useCallback, useEffect, useState } from 'react'
import { api, type Session } from '../api/client'

export function useSession() {
  const [session, setSession] = useState<Session | null>(null)
  const [loading, setLoading] = useState(true)
  const [ssoSite, setSsoSite] = useState<string | null>(null)

  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const token = params.get('token')

    if (token) {
      const company = params.get('company') ?? undefined
      const site = params.get('site') ?? undefined
      // Clean the URL immediately so a page refresh never re-sends an
      // already-used (and by then likely expired) token.
      window.history.replaceState({}, '', window.location.pathname)

      api
        .ssoLogin(token, company, site)
        .then((ssoSession) => {
          setSession(ssoSession)
          if (site) setSsoSite(site)
        })
        .catch(() => setSession(null))
        .finally(() => setLoading(false))
      return
    }

    api
      .me()
      .then(setSession)
      .catch(() => setSession(null))
      .finally(() => setLoading(false))
  }, [])

  const signOut = useCallback(async () => {
    await api.logout()
    setSession(null)
    setSsoSite(null)
  }, [])

  return { session, setSession, loading, signOut, ssoSite }
}
```

- [ ] **Step 3: Modificar `App.tsx`**

En `src/web/src/App.tsx`, cambiar el import para incluir `api` y `useEffect`:

```typescript
import { useEffect, useState } from 'react'
import { api, type Context, type Vendor } from './api/client'
```

Cambiar la línea que desestructura `useSession()` y agregar el efecto de auto-selección de planta, justo después:

```typescript
  const { session, setSession, loading, signOut, ssoSite } = useSession()
  const [context, setContext] = useState<Context | null>(null)
  const [vendor, setVendor] = useState<Vendor | null>(null)
  const [rows, setRows] = useState<PartRowState[]>([])
  const [activeTab, setActiveTab] = useState<'nueva' | 'historial'>('nueva')

  useEffect(() => {
    if (!session?.company || !ssoSite || context) return

    api
      .setContext(ssoSite)
      .then(setContext)
      .catch(() => {
        // The site hint from Kinetic isn't a valid plant for this user/company
        // — fall through to the normal ContextPicker, no error shown.
      })
  }, [session?.company, ssoSite, context])
```

- [ ] **Step 4: Verificar que compila**

Run: `npm run build` (desde `src/web`)
Expected: compila sin errores de TypeScript.

- [ ] **Step 5: Verificar en el navegador — camino manual (regresión)**

Con el backend corriendo y el frontend en modo dev, entra a la URL normal (sin query params) y confirma que el login manual (usuario/contraseña) sigue funcionando exactamente igual que antes.

- [ ] **Step 6: Verificar en el navegador — camino SSO simulado**

Sin depender del servidor real de Epicor todavía: arranca el backend con una `EpicorTokenOptions:SignKey` de prueba en `appsettings.Development.json`, genera un token válido a mano (puedes usar un script corto de PowerShell/Node que replique `EpicorTokenService.IssueSessionToken`, o temporalmente loguear el resultado de `IssueSessionToken` desde un endpoint de prueba local), y navega a `https://localhost:<puerto>/?token=<ese-token>`. Confirma que entra directo a la pantalla de selección de compañía (o de trabajo, si el `company` en la URL era válido) sin mostrar el formulario de login.

- [ ] **Step 7: Verificar en vivo contra Epicor real**

Con la Sign Key **real** configurada en `appsettings.Development.json` (la de Epicor Server Management), publica o corre la app apuntando al servidor real, da click en el menú "OC Automatica Kinetic" desde Kinetic, y confirma que entra directo sin pedir usuario/contraseña. Si `company`/`site` coinciden con los del token, confirma que también evitó los selectores de compañía/planta.

- [ ] **Step 8: Commit**

```bash
git add src/web/src/api/client.ts src/web/src/auth/useSession.ts src/web/src/App.tsx
git commit -m "feat: detect an Epicor SSO token in the URL and skip manual login"
```

---

## Verificación final

- [ ] `dotnet test` desde la raíz del repo — todo pasa (todos los existentes + los nuevos de las Tasks 1-4).
- [ ] `npm run build` en `src/web` — compila sin errores.
- [ ] Prueba manual end-to-end contra el servidor real de Epicor (Task 5, Step 7): click en el menú de Kinetic → entra a OCAutomatica sin pedir credenciales → compañía/planta auto-seleccionadas cuando corresponde.
- [ ] Login manual (usuario/contraseña) sigue funcionando exactamente igual que antes de este plan.
- [ ] **Riesgo conocido, verificar después de que todo lo anterior funcione:** comparar la lista de Sessions en el Epicor Admin Console antes y después de una sesión SSO activa, para confirmar si consume o no una licencia separada de la sesión nativa de Kinetic (ver spec, sección "Fuera de alcance / riesgos conocidos").
