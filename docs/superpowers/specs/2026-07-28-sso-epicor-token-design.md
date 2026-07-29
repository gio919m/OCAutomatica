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
3. **Se reutiliza el token original de Kinetic para toda la sesión — nunca se re-emite uno propio.** *(Revisado en vivo tras el despliegue — ver "Fuera de alcance / riesgos conocidos" para la evidencia.)* Se probó primero re-emitir un JWT propio de 8 horas, pero se confirmó con el Admin Console de Epicor que un token que esta app firma por su cuenta (aunque sea para el mismo usuario) abre una sesión/licencia de Epicor **aparte** de la nativa de Kinetic, mientras que el token **original** —el mismo que ya usa la sesión de Kinetic abierta en el navegador— no abre ninguna. La cookie de sesión de OCAutomatica ahora dura exactamente lo que le queda de vida a ese token original (hoy, hasta 1 hora — lo que Epicor tenga configurado en Token Authentication → Lifetime), no un valor fijo de 8 horas. Pasado ese tiempo, el usuario tiene que volver a dar click al menú de Kinetic para renovar la sesión.
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
          → SessionStore.Create con el mismo BearerToken original (no password, no token propio)
          → auto-selecciona company si es válida para el usuario
          → EstablishEpicorSessionAsync (Ice.Lib.SessionModSvc/Login, igual que hoy, con el token original)
          → set-cookie de sesión con MaxAge = vigencia restante del token original
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
}
```
Se lee de `appsettings` bajo la sección `EpicorToken`, con el mismo tratamiento de secreto que hoy tiene `Epicor:ApiKey` (nunca en el cliente, nunca en logs).

**`src/OCAutomatica.Api/Epicor/IEpicorTokenService.cs`**
```csharp
public interface IEpicorTokenService
{
    bool TryValidate(string token, out string username, out DateTimeOffset expiresAtUtc);
}
```
*(Ajustado tras la revisión en vivo: ya no existe `IssueSessionToken` — este servicio solo valida, nunca emite. Ver Decisión de diseño 3.)*

**`src/OCAutomatica.Api/Epicor/EpicorTokenService.cs`**
- `TryValidate`: decodifica el JWT, verifica la firma HMACSHA256 con la Sign Key, verifica `iss == "epicor"` y `aud == "epicor"`, parsea `exp` (string → long) y confirma que no haya pasado. Si la Sign Key configurada está vacía, rechaza de inmediato (nunca valida "en falso" contra una llave vacía). Si cualquier verificación falla, regresa `false`, `username` vacío y `expiresAtUtc = DateTimeOffset.MinValue` — nunca lanza excepción por un token malformado (es una entrada no confiable del cliente). Cuando el token es válido, `expiresAtUtc` expone su `exp` real, para que el llamador pueda alinear su propia sesión a esa misma vigencia.

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
- Pasos: `IEpicorTokenService.TryValidate` (401 si falla; expone también `expiresAtUtc`) → construir `EpicorCredentials` con `BearerToken` = el token **original** → `IAuthService.ValidateAsync` (reusa la lógica existente sin cambios, ya que solo depende de `IEpicorClient`, agnóstico al tipo de credencial) → si `companies` es null o vacío, mismo manejo de error que ya existe en `Login` → `_sessions.Create` con esas mismas credenciales (sin re-emitir nada) → si `request.Company` está en la lista de companies del usuario, fijar contexto y llamar `EstablishEpicorSessionAsync` (mismo método privado ya existente, sin cambios, usando el token original) → set-cookie con `MaxAge = expiresAtUtc - ahora` (la vigencia real que le queda al token, no un valor fijo) → devolver el mismo `SessionResponse` (sin `Plant` fijado todavía — ver nota de frontend abajo).
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

- `EpicorTokenServiceTests`: valida un token bien firmado (construido de forma independiente al servicio, nunca contra su propio encoder); rechaza firma inválida (Sign Key distinta); rechaza `exp` pasado; rechaza `iss`/`aud` distintos de `"epicor"`; rechaza cuando la Sign Key configurada está vacía; expone correctamente el `expiresAtUtc` del token válido.
- `EpicorClientTests`: con `BearerToken` presente, el request armado lleva `Authorization: Bearer <token>` y NO lleva el header Basic; sin `BearerToken`, comportamiento idéntico al actual (regresión).
- `SessionStoreTests`: `Create`+`GetCredentials` con credenciales Bearer devuelve el `BearerToken` correcto y `Password` vacío; con credenciales de password, comportamiento idéntico al actual (regresión).
- Integration test (`AuthControllerTests` o similar) para `POST /api/auth/sso-login`: token válido → 200 + cookie de sesión + `SessionResponse` correcto; token inválido/expirado → 401; `company`/`site` inexistentes para el usuario → 200 pero sin compañía/planta fijada (cae a selector).

## Restricciones globales

- La Sign Key (`EpicorToken:SignKey`) nunca se expone al cliente, nunca se loggea, y se trata con el mismo cuidado que `Epicor:ApiKey` y la cadena de conexión SQL ya existentes en `appsettings`.
- El comportamiento del login manual existente (endpoint, UI, mensajes) no cambia.
- Este servicio nunca emite tokens propios — solo valida los que ya llegaron firmados por Epicor. Ver Decisión de diseño 3.

## Fuera de alcance / riesgos conocidos (no bloquean esta implementación)

- **Consumo de licencia — CONFIRMADO en producción, con dos causas distintas encontradas y ambas mitigadas.** Se probó en vivo contra el Admin Console de Epicor (Sessions), aislando variables una por una:
  1. Reutilizar el token **original** de Kinetic (en vez de que esta app emita uno propio — ver Decisión de diseño 3) por sí solo no evitó la sesión extra.
  2. La causa real: `EstablishEpicorSessionAsync` llama a `Ice.Lib.SessionModSvc/Login`, cuyo propósito **es literalmente crear una sesión nueva y devolver su ID** — la llama siempre abre una sesión/licencia aparte, sin importar qué token se use para autenticar esa llamada. Se confirmó con una prueba controlada: una llamada REST pura desde PowerShell (sin navegador, sin cookies) con el token original **no** abrió sesión nueva mientras no se llamara a `SessionModSvc/Login`; en cuanto esta app volvía a llamarlo, aparecía la sesión extra de inmediato.
  - **Mitigación aplicada:** `EstablishEpicorSessionAsync` y `ReleaseEpicorSessionAsync` ahora son no-op cuando las credenciales son Bearer (SSO) — nunca se llama a `SessionModSvc/Login` ni `Logout` para una sesión SSO. El login manual (password) sigue llamando a ambos exactamente igual que siempre, ya que ahí sí existe una sesión propia que abrir y cerrar.
  - **Riesgo que esto evita:** llamar a `Logout` sin un `SessionInfo` que apunte a una sesión específica (que es lo que pasaría si se llamara para una sesión SSO, ya que nunca se estableció una propia) podría cerrar la sesión implícita ligada a ese Bearer token — que podría ser la sesión nativa de Kinetic del usuario. Por eso el no-op cubre ambos lados (Login y Logout), no solo uno.
  - Lo que **no** se resuelve: cualquier llamada REST de negocio normal (GetRows, etc.) sigue siendo, en general, una interacción distinta de la sesión nativa a nivel de Epicor — lo que se logró es específicamente no *agregar* una sesión nueva de licencia por el mecanismo de login/logout explícito que esta app misma decidía invocar.
- **Higiene de secretos en `appsettings`**: ya existe una brecha conocida y pendiente (contraseña de `sa` de SQL Server en texto plano, ver specs anteriores) — este diseño no la agrava ni la resuelve; la Sign Key se suma al mismo problema de fondo, ya identificado.
- Cambios al puente `WebLink/Index.html` o a la configuración del menú en Kinetic: no forman parte de este plan (son propiedad de otro compañero/otro sistema); el diseño asume que la URL configurada hoy (`.../8443/?...`) sigue apuntando a la raíz de producción de OCAutomatica sin cambios.
- **El token original viaja en un query string de la URL**: el puente WebLink navega a `.../?token=...`, y ese token queda expuesto en logs de servidor (IIS W3C logging registra `cs-uri-query` por defecto) y en el header `Referer` de las peticiones que el navegador hace antes de que `window.history.replaceState` limpie la URL (los recursos estáticos de `index.html` — script, CSS, favicon — ya salieron con el `Referer` completo). Mitigación pendiente de decidir con el dueño del puente WebLink: pedirle que use un fragmento de URL (`#token=...`, nunca enviado al servidor) en vez de un query param, y/o desactivar el logging de query string para este sitio en IIS.
- **Al desplegar a producción, agregar la sección `EpicorToken` (con la Sign Key real) al `appsettings.Production.json` que se sube al servidor** — no viene incluida por defecto y `EpicorTokenService.TryValidate` rechaza explícitamente cualquier intento de validación si `SignKey` está vacío, así que sin este paso el login SSO simplemente no funcionará (fallará de forma segura, no insegura).
- **La sesión de OCAutomatica dura como máximo lo que dure el token de Kinetic** (hoy, hasta 1 hora — lo que Epicor tenga configurado en Token Authentication → Lifetime). Si Epicor cambia esa configuración, la duración de la sesión de esta app cambia con ella automáticamente (se lee de `expiresAtUtc`, nunca un valor fijo en este código) — no requiere ningún cambio de código si Epicor ajusta su Lifetime.
