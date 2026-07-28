# SSO desde Epicor Kinetic vía Token Authentication — Design

## Goal

Permitir que un usuario que ya inició sesión en Epicor Kinetic entre a OCAutomatica sin volver a teclear su usuario/contraseña de Epicor, lanzando la app desde un menú de Kinetic (Program Type "Web Bridge"). El login manual (usuario/contraseña) existente se mantiene sin cambios como alternativa.

## Contexto confirmado en vivo

- Epicor Kinetic 2026.1 (Application Server `Kinetic2026_1`) tiene habilitada la función nativa **"Token Authentication"** (Server Management → Application Server → Configure Token Authentication): una **Sign Key compartida (HS256, Base64)** y un **Lifetime de 3600 segundos**.
- Un compañero del equipo construyó un puente estático (`C:\inetpub\wwwroot\WebLink\Index.html`, en el mismo IIS que Kinetic) que: lee la cookie de sesión de Kinetic (patrón de nombre `*token.auth`, valor JSON `{userId, token}`), y redirige el navegador a una URL configurable (`?url=...`) agregando `token`, `userId`, `menu`, `company`, `site`, `channelid` como query params.
- El menú de Kinetic ("OC Automatica Kinetic", Program Type "Web Bridge") ya está configurado con `url=https://srvcsjpr2.carnessanjuan.local:8443/` — la raíz de producción de OCAutomatica. No requiere cambios para este diseño.
- Se confirmó en vivo contra la REST API real (`Erp.BO.PartSvc/Parts` vía Swagger) que ese JWT autentica llamadas `GetRows` reales (200 con datos), y que sin el header `Authorization` Epicor exige login (401 con prompt de Basic Auth) — confirmando que el JWT es aceptado como autenticación real, no solo un adorno.
- Formato del JWT observado: header `{"alg":"HS256","typ":"JWT"}`, payload `{"exp":"<epoch>","iat":"<epoch>","iss":"epicor","aud":"epicor","username":"<usuario>"}`. **`exp`/`iat` van codificados como strings, no como números** — cualquier token que este diseño emita debe replicar ese formato exacto para que Epicor lo acepte.

## Decisiones de diseño

1. **Login manual se mantiene** tal cual, sin cambios de comportamiento. El SSO es puramente aditivo.
2. **Bearer JWT directo para las llamadas REST** de una sesión SSO — nunca se pide ni se guarda la contraseña real del usuario para este camino.
3. **Re-emisión propia del token**: al validar el JWT que llega desde Kinetic, el backend firma su propio JWT (mismo formato, misma Sign Key, mismo `username`) con una vigencia de 8 horas (igual que la cookie de sesión actual), en vez de usar el original de 1 hora tal cual. Esto evita que el usuario se desconecte a media tarea.
4. **Auto-selección de compañía/planta** desde los parámetros `company`/`site` de la URL, siempre validados contra las compañías/plantas reales del usuario (nunca se confía a ciegas en un query param sin firmar). Si no coinciden, se cae al selector manual existente.
5. **Sin pantalla de confirmación**: un token válido entra directo a la pantalla de trabajo. Un token ausente/inválido/expirado cae en silencio al formulario de login manual, sin mensaje de error visible.

## Arquitectura

Se agrega un segundo camino de entrada, en paralelo al login manual. La app hoy no usa ningún router de cliente (una sola pantalla condicionada por el estado de sesión), así que el punto de entrada SSO es el arranque de React revisando `window.location.search` por un parámetro `token`.

```
Kinetic (menú "Web Bridge")
  → WebLink/Index.html (fuera de este repo, ya construido)
    → lee cookie *token.auth de Kinetic
    → redirige a https://.../?token=...&company=...&site=...
      → React (arranque): detecta `token` en la URL
        → POST /api/auth/sso-login { token, company, site }
          → valida firma JWT (Sign Key) + expiración + iss/aud
          → username = claim del JWT
          → AuthService.ValidateAsync (reusa lógica existente, ahora vía Bearer)
          → re-emite JWT propio (8h) con IEpicorTokenService.IssueSessionToken
          → SessionStore.Create con BearerToken (no password)
          → auto-selecciona company si es válida para el usuario
          → EstablishEpicorSessionAsync (Ice.Lib.SessionModSvc/Login, igual que hoy)
          → set-cookie de sesión (igual que login manual)
        ← SessionResponse (mismo shape que login manual)
      → si Company quedó fijada y hay `site` en la URL:
          POST /api/organization/context { plant: site } (endpoint ya existente)
      → React entra directo a la pantalla de trabajo
```

## Componentes

### Backend — nuevos

**`src/OCAutomatica.Api/Epicor/EpicorTokenOptions.cs`**
```csharp
public sealed class EpicorTokenOptions
{
    public const string SectionName = "EpicorToken";

    /// <summary>Sign Key configurada en Epicor Server Management → Token Authentication (Base64).</summary>
    public string SignKey { get; set; } = string.Empty;

    /// <summary>Vigencia (segundos) de los tokens que esta app re-emite. Debe igualar la MaxAge de la cookie de sesión (8h = 28800).</summary>
    public int SessionLifetimeSeconds { get; set; } = 28800;
}
```
Se lee de `appsettings` bajo la sección `EpicorToken`, con el mismo tratamiento de secreto que hoy tiene `Epicor:ApiKey` (nunca en el cliente, nunca en logs).

**`src/OCAutomatica.Api/Epicor/IEpicorTokenService.cs`**
```csharp
public interface IEpicorTokenService
{
    bool TryValidate(string token, out string username);
    string IssueSessionToken(string username);
}
```

**`src/OCAutomatica.Api/Epicor/EpicorTokenService.cs`**
- `TryValidate`: decodifica el JWT, verifica la firma HMACSHA256 con la Sign Key, verifica `iss == "epicor"` y `aud == "epicor"`, parsea `exp` (string → long) y confirma que no haya pasado. Si cualquier verificación falla, regresa `false` y `username` vacío — nunca lanza excepción por un token malformado (es una entrada no confiable del cliente).
- `IssueSessionToken(username)`: construye un JWT con el mismo formato exacto observado (`iss`/`aud`/`username` como strings, `exp`/`iat` como strings de epoch), firmado con la misma Sign Key, con `exp = ahora + SessionLifetimeSeconds`.
- **Regla de confianza no negociable**: `IssueSessionToken` solo se invoca desde `sso-login` con un `username` que salió de un `TryValidate` exitoso sobre un token que sí llegó firmado por Epicor. Nunca se expone como una operación que acepte un username arbitrario del cliente — eso sería una forma de suplantación total, ya que la Sign Key firma para cualquier usuario sin verificar contraseña.

### Backend — modificados

**`src/OCAutomatica.Api/Epicor/EpicorCredentials.cs`**
- Se agrega una propiedad opcional, siguiendo el mismo patrón que `EpicorSessionId`:
```csharp
public string? BearerToken { get; init; }
```

**`src/OCAutomatica.Api/Epicor/EpicorClient.cs`** (`AddAuthHeaders`)
- Si `credentials.BearerToken` no es nulo/vacío, manda `Authorization: Bearer <token>` en vez de construir el header Basic Auth a partir de `Username`/`Password`. El header `x-api-key` se sigue mandando siempre, sin importar el modo. El header `SessionInfo` (para `EpicorSessionId`) sigue funcionando igual, independiente del modo de autenticación.

**`src/OCAutomatica.Api/Auth/ISessionStore.cs` / `SessionStore.cs`**
- El `Entry` interno hoy solo guarda `ProtectedPassword`. Se cambia a guardar un secreto genérico protegido (`ProtectedSecret`) más un discriminador (`IsBearer: bool`).
- `Create(EpicorCredentials credentials)`: si `credentials.BearerToken` está presente, protege y guarda ese valor con `IsBearer = true`; si no, protege `credentials.Password` con `IsBearer = false` (comportamiento actual, sin cambios de resultado).
- `GetCredentials(sessionId)`: si `IsBearer`, reconstruye `new EpicorCredentials(entry.Session.Username, string.Empty) { BearerToken = secretoDescifrado }`; si no, reconstruye igual que hoy con `Password`.

**`src/OCAutomatica.Api/Controllers/AuthController.cs`**
- Nueva acción:
```csharp
public sealed record SsoLoginRequest(string Token, string? Company, string? Site);

[HttpPost("sso-login")]
public async Task<IActionResult> SsoLogin(SsoLoginRequest request, CancellationToken ct)
```
- Pasos: `IEpicorTokenService.TryValidate` (401 si falla) → construir `EpicorCredentials` con `BearerToken` → `IAuthService.ValidateAsync` (reusa la lógica existente sin cambios, ya que solo depende de `IEpicorClient`, agnóstico al tipo de credencial) → si `companies` es null o vacío, mismo manejo de error que ya existe en `Login` → `IEpicorTokenService.IssueSessionToken(username)` para obtener el token de 8h → `_sessions.Create` con las credenciales Bearer re-emitidas → si `request.Company` está en la lista de companies del usuario, fijar contexto y llamar `EstablishEpicorSessionAsync` (mismo método privado ya existente, sin cambios) → set-cookie igual que `Login` → devolver el mismo `SessionResponse` (sin `Plant` fijado todavía — ver nota de frontend abajo).
- **`Site`/planta**: esta acción NO intenta resolver la planta ni el comprador — eso ya lo hace `OrganizationController.SetContext` (`POST /api/organization/context`), que internamente resuelve el `BuyerId` a partir del `Plant`. Duplicar esa resolución dentro de `AuthController` sería reinventar lógica que ya existe. En su lugar, el auto-select de planta ocurre del lado del frontend (ver abajo).

### Frontend — nuevos/modificados

**`src/web/src/api/client.ts`**
- Nuevo método `api.auth.ssoLogin(token: string, company?: string, site?: string): Promise<SessionResponse>` que llama a `POST /api/auth/sso-login`.

**Punto de entrada de la app** (componente raíz, ej. `App.tsx`)
- Al montar: lee `token`, `site` de `window.location.search`. Si hay `token`, llama a `ssoLogin(token, company, site)`.
  - En éxito: si el `SessionResponse` ya trae `Company` fijada y había un `site` en la URL, llama automáticamente a `api.organization.setContext(site)` (el mismo endpoint que usa el selector manual de planta) antes de mostrar cualquier pantalla. Si esa llamada falla (planta inválida para el usuario), se ignora el error y se muestra el selector de planta normal — igual que si el usuario nunca hubiera tenido una planta preseleccionada.
  - Si `Company` no se fijó (el `company` de la URL no era válido para el usuario), se muestra el selector de compañía normal, ignorando `site` por completo (no tiene sentido fijar planta sin compañía).
  - En error de `ssoLogin`: no se muestra nada especial — simplemente se renderiza el formulario de login manual, como si `token` nunca hubiera estado presente.
- Si no hay `token` en la URL, comportamiento actual sin cambios.

## Manejo de errores

- Token con firma inválida, expirado, o emisor/audiencia incorrectos → `TryValidate` regresa `false` → `sso-login` regresa 401 → frontend cae en silencio al login manual.
- `username` del token válido pero sin compañías asignadas en Epicor, o error de Epicor al consultarlas → mismo manejo (mismos códigos y mensajes) que ya existe hoy en `Login` para esos casos.
- `company`/`site` de la URL no coinciden con ninguna compañía/planta real del usuario → no se auto-selecciona nada; se muestra el selector manual existente (nunca es un error bloqueante).
- Fallo al establecer la sesión explícita de Epicor (`SessionModSvc/Login`) → mismo comportamiento *best-effort* ya existente (no bloquea el login).

## Testing

- `EpicorTokenServiceTests`: firma y valida un token propio correctamente; rechaza firma inválida (Sign Key distinta); rechaza `exp` pasado; rechaza `iss`/`aud` distintos de `"epicor"`; `IssueSessionToken` produce un token que el propio `TryValidate` acepta (round-trip).
- `EpicorClientTests`: con `BearerToken` presente, el request armado lleva `Authorization: Bearer <token>` y NO lleva el header Basic; sin `BearerToken`, comportamiento idéntico al actual (regresión).
- `SessionStoreTests`: `Create`+`GetCredentials` con credenciales Bearer devuelve el `BearerToken` correcto y `Password` vacío; con credenciales de password, comportamiento idéntico al actual (regresión).
- Integration test (`AuthControllerTests` o similar) para `POST /api/auth/sso-login`: token válido → 200 + cookie de sesión + `SessionResponse` correcto; token inválido/expirado → 401; `company`/`site` inexistentes para el usuario → 200 pero sin compañía/planta fijada (cae a selector).

## Restricciones globales

- La Sign Key (`EpicorToken:SignKey`) nunca se expone al cliente, nunca se loggea, y se trata con el mismo cuidado que `Epicor:ApiKey` y la cadena de conexión SQL ya existentes en `appsettings`.
- El comportamiento del login manual existente (endpoint, UI, mensajes) no cambia.
- `IssueSessionToken` solo se invoca internamente tras un `TryValidate` exitoso sobre un token ya firmado por Epicor — nunca a partir de un username enviado libremente por el cliente.

## Fuera de alcance / riesgos conocidos (no bloquean esta implementación)

- **Consumo de licencia**: no se ha verificado empíricamente si una sesión REST autenticada por Bearer JWT consume una licencia de Epicor separada de la sesión nativa de Kinetic que originó el token. Queda como verificación a hacer una vez la funcionalidad esté construida (comparar la lista de Sessions en el Admin Console antes/después de una sesión SSO activa).
- **Higiene de secretos en `appsettings`**: ya existe una brecha conocida y pendiente (contraseña de `sa` de SQL Server en texto plano, ver specs anteriores) — este diseño no la agrava ni la resuelve; la Sign Key se suma al mismo problema de fondo, ya identificado.
- Cambios al puente `WebLink/Index.html` o a la configuración del menú en Kinetic: no forman parte de este plan (son propiedad de otro compañero/otro sistema); el diseño asume que la URL configurada hoy (`.../8443/?...`) sigue apuntando a la raíz de producción de OCAutomatica sin cambios.
