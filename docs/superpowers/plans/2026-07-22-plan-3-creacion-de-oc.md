# OC Automática — Plan 3: Creación de la orden de compra

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **Tasks 2 and 3 are manual Epicor-configuration tasks, not coding tasks — they cannot be dispatched to a coding subagent.**

**Goal:** Un comprador captura comentarios, ve los Cambios Físicos pendientes del proveedor, y al presionar "Procesar" se crea en Epicor una orden de compra **aprobada**, transaccional, con el BuyerID correcto — o recibe un error descriptivo si algo falla, sin dejar una OC a medias.

**Architecture:** Se agrega una nueva capacidad a `IEpicorClient` para invocar **Epicor Functions** (mecanismo REST distinto de las BAQs/BO usadas en los Planes 1-2 — confirmado en la guía oficial, sección "Invoking Epicor Functions"). La creación de la OC se delega a una única Function (`OCACrearOC`) que ejecuta toda la secuencia `GetNewPOHeader → ChangeVendor → ... → ChangeApproveSwitch → Update` dentro de una transacción del lado de Epicor — resolviendo de una vez la falta de atomicidad del código legacy (defecto 10.3) y evitando ~160 llamadas HTTP desde el backend. Los Cambios Físicos pendientes se consultan dos veces, independientemente, por diseño (ver spec, sección 6): un BAQ para la vista previa en la interfaz, y la propia Function para construir el `CommentText` en el momento de crear la orden — así el comentario nunca depende de lo que la interfaz llegó a mostrar.

**Tech Stack:** .NET 8 y React 19 + TypeScript ya en uso (Planes 1-2). Sin librerías nuevas.

## Cómo invocar una Epicor Function por REST (verificado contra la guía oficial, no es una suposición)

Confirmado en `Kinetic_RESTServicesGuideV2_2026.100.pdf`, sección "Invoking Epicor Functions" (páginas 37-39):

```
POST {BaseUrl}/api/v2/efx/{Company}/{Library}/{Function}/
Authorization: Basic <user:pass en base64>
x-api-key: <api key>
Content-Type: application/json

{ "Input1": valor, "Input2": valor, ... }
```

- **Nunca** va bajo `/api/v2/odata/` — es un segmento de URL completamente distinto (`/api/v2/efx/`). El `EpicorClient` actual (Planes 1-2) construye siempre `/api/v2/odata/{company}/{relativePath}` dentro de `BuildRequest` — **no puede reutilizarse tal cual** para Functions; hace falta un método nuevo.
- El body es un objeto JSON plano con los parámetros de entrada de la Function, nombrados exactamente como los parámetros definidos en el Function Designer.
- La respuesta exitosa es un objeto JSON con los parámetros de salida de la Function (los que se nombren al definirla — no hay un nombre fijo como "result", eso era solo el ejemplo genérico de la guía).
- Un error de negocio dentro de la Function (p. ej. una validación que la detiene) se transmite como una excepción — mapea al mismo patrón `EpicorException`/`ErrorMessage` ya usado para BOs (confirmado en la sección "Mapping Between Kinetic Service Exceptions and HTTP Error Codes" de la misma guía: `Ice.Common.BusinessObjectException` → 400, con `ErrorMessage` legible). El `EpicorClient` actual ya sabe parsear esa forma — no hace falta lógica nueva de manejo de errores, solo el nuevo método para construir la URL/request correctos.
- Para que la Function sea invocable desde la compañía activa, su librería debe estar mapeada a esa compañía en Library Maintenance > Security — **si la Task 3 devuelve 404 al probarla, revisa esto primero**, es el error más común documentado en la guía para este caso.

## Supuestos y pendientes (descubiertos al leer `btnProc_Click` completo)

- **`ConfirmCurrentOC()`** — método invocado antes de crear la OC ("Se cancelo el proceso" si devuelve `false`); no se revisó su cuerpo. Si hace alguna validación de negocio que no esté ya cubierta por `PartStatusValidationMessages` o por las reglas de Epicor, dímelo antes de dar por cerrada la Task 3 — si no, se asume que no aporta nada que la nueva arquitectura no cubra ya (sesión válida, proveedor seleccionado, líneas con cantidad/costo > 0, ya validado en el Plan 2).
- **`AsignarValoresATextBox(vendID)`** — el método cuyo resultado sobrescribe el comentario real (defecto 10.6); tampoco se revisó su cuerpo, pero no hace falta: la Task 3 no lo traduce, construye el comentario una sola vez con el query de Cambios Físicos ya confirmado.
- **Hallazgo nuevo, no listado en los 8 defectos originales del spec:** el legacy llama a `PartStatusValidationMessages` pero nunca revisa su resultado (`result10`/`questionString`/`msgType` se descartan) — una parte bloqueada podría colarse en la orden sin aviso. La Task 3 lo corrige revisando el resultado y deteniendo la transacción si indica un bloqueo.

## Global Constraints

- **.NET 8** (`net8.0`). No usar preview features.
- **Epicor REST v2** únicamente. Ninguna conexión directa a SQL Server.
- **Ninguna credencial en código ni en `appsettings.json` versionado.**
- **El navegador nunca recibe credenciales de Epicor.**
- **NO usar FluentAssertions.** Solo asserts de xUnit.
- Ambiente de desarrollo: **el Epicor de pruebas** (`CFSJ_LAF`), nunca producción.
- Mensajes de la interfaz en **español**; código y comentarios en **inglés**.
- Cantidades y costos son **decimales** en todo el flujo — nunca truncados a `int` (defecto 10.1, ya corregido en el Plan 2 para el grid; esta regla se extiende al envío hacia `OCACrearOC`).
- Toda llamada a `IEpicorClient` con un valor interpolado en la URL debe escapar ese valor con `Uri.EscapeDataString` — el cliente no encapa nada por su cuenta (hallazgo real del Plan 2).
- Seguir los patrones ya establecidos: servicios con `IEpicorClient` inyectado; controladores que verifican `HttpContext.Items[SessionMiddleware.ItemKey]`, resuelven credenciales con `_sessions.GetCredentials(session.SessionId)`, y atrapan `EpicorException` mapeando `ex.Reason` a un mensaje en español vía un helper `HandleEpicorException` — **excepto en `PurchaseOrdersController`, donde el motivo `Other` debe mostrar el mensaje real de Epicor en vez de uno genérico** (ver Task 5 — es un requisito explícito del spec: "Salida: `poNum`, o error descriptivo").
- **Cualquier clase de test que implemente `IEpicorClient` directamente debe actualizarse** al agregar el método nuevo a la interfaz (Task 1) — ver la lista exacta de archivos afectados en esa tarea. Si se omite alguno, la solución completa deja de compilar.

---

## Contexto: interfaces ya existentes de los Planes 1 y 2 (no se repiten, solo se consumen)

- `IEpicorClient.GetAsync<T>(company, relativePath, credentials, ct)` / `PostAsync<T>(company, relativePath, body, credentials, ct)` — `src/OCAutomatica.Api/Epicor/`
- `EpicorException { int StatusCode; EpicorErrorReason Reason; string Message; }`, `EpicorErrorReason { InvalidCredentials, InvalidApiKey, AccessDenied, Other }`
- `EpicorCredentials(string Username, string Password)`
- `ISessionStore.GetCredentials(string sessionId)` → `EpicorCredentials?`
- `UserSession { string SessionId; string Username; string Company; string Plant; string? BuyerId; }` — **`BuyerId` ya viene resuelto desde Buyer Maintenance** (Plan 1, `IBuyerService.ResolveDefaultBuyerAsync`) y ya se guarda en la sesión al fijar contexto (`OrganizationController.SetContext`). Este plan **no vuelve a resolver el buyer** — solo lee `session.BuyerId`.
- `SessionMiddleware.ItemKey`
- `IPartService.GetByVendorAsync` → `PartRow` (Plan 2) — cantidades/costo como `decimal`.
- Frontend: `src/web/src/api/client.ts` exporta `api = { companies, login, logout, me, plants, setContext, vendors, parts }`, la clase `ApiError { status }`, e interfaces `Context { company, plant, buyerId, buyerName, canCreateOrders }`, `Vendor`, `PartRow`.
- `src/web/src/parts/PartsGrid.tsx` exporta `PartsGrid` (prop `vendorId`) y el tipo `PartRowState extends PartRow { assign: boolean; qtyToFill: number | null }` — hoy **autocontenido**, no expone sus filas hacia afuera (Task 6 de este plan lo extiende).
- `src/web/src/App.tsx` renderiza `LoginPage → ContextPicker → VendorSearch + PartsGrid` (estado actual, que la Task 8 de este plan extiende).

---

## Query real de Cambios Físicos (confirmado contra `btnProc_Click`)

El query legacy exacto que arma el texto "Cambios Fisicos: ..." es:

```sql
select convert(varchar,uda.character01,(100)) +'  '+ convert(varchar,uda.character02,(100)) +'  '+  convert(varchar,uda.character04,(100)) +'  '+
convert(varchar,convert(decimal(22,2),(sum(uda.number01))),(100)) +'  '+
convert(varchar,uda.character06,(100))
from ice.ud104a uda
inner join ice.UD104 ud on uda.Company=ud.Company and uda.Key1=ud.Key1
where uda.character06 = 'PENDIENTE' and uda.character10 = @vendorId and uda.company = @company and ud.ShortChar01 = @plant
group by uda.character01, uda.character02, uda.character03, uda.character04, uda.character06
```

Notas importantes que esto revela, y que corrigen el diseño original de este plan:

- La tabla real es **`UD104A`** (no `UD104` directamente) — `UD104` solo se une para leer `ShortChar01` (la **planta**, campo del padre).
- El vendedor se filtra por `UD104A.Character10`, no por un campo distinto.
- No hay campos "número de parte"/"descripción" separados — el legacy nunca les da significado, solo concatena `Character01`, `Character02`, `Character04`, la suma de `Number01`, y `Character06` (siempre `'PENDIENTE'`, incluido igual en el texto). Este plan traduce el query **tal cual**, sin inventar una estructura semántica que el propio legacy no tiene.
- `Character06` se repite en el texto de cada línea aunque siempre valga `'PENDIENTE'` (es literalmente el valor del filtro) — se conserva por fidelidad, aunque parezca redundante.

---

## Estructura de archivos

```
src/OCAutomatica.Api/
├── Epicor/
│   ├── IEpicorClient.cs           # (modificar: agregar InvokeFunctionAsync<T>)
│   └── EpicorClient.cs             # (modificar: implementar InvokeFunctionAsync, refactor de headers compartido)
├── PurchaseOrders/
│   ├── PurchaseOrderModels.cs
│   ├── IPurchaseOrderService.cs
│   ├── PurchaseOrderService.cs
│   ├── ICambiosFisicosService.cs
│   └── CambiosFisicosService.cs
├── Controllers/
│   ├── PurchaseOrdersController.cs    # POST /api/purchase-orders
│   └── CambiosFisicosController.cs    # GET /api/cambios-fisicos?vendorId=
└── Program.cs                         # (modificar)

src/web/src/
├── api/client.ts                      # (modificar: PurchaseOrderLine, CreatePurchaseOrderResult, CambioFisico, api.purchaseOrders, api.cambiosFisicos)
├── parts/PartsGrid.tsx                # (modificar: prop onRowsChange, exportar isRowInvalid)
├── purchaseOrders/
│   └── PurchaseOrderPanel.tsx         # comentarios + Cambios Fisicos + boton Procesar
└── App.tsx                            # (modificar: integracion final)

tests/OCAutomatica.Api.Tests/
├── Epicor/EpicorClientTests.cs         # (modificar: agregar tests de InvokeFunctionAsync)
├── Vendors/VendorServiceTests.cs       # (modificar: agregar stub de InvokeFunctionAsync — solo para compilar)
├── Parts/PartServiceTests.cs           # (modificar: idem)
├── Buyers/BuyerServiceTests.cs         # (modificar: idem)
├── Auth/AuthServiceTests.cs            # (modificar: idem)
├── Organization/OrganizationServiceTests.cs  # (modificar: idem)
├── Integration/TestEpicorClient.cs     # (modificar: idem)
├── PurchaseOrders/PurchaseOrderServiceTests.cs   # (nuevo)
└── PurchaseOrders/CambiosFisicosServiceTests.cs  # (nuevo)
```

---

## Task 1: Extender `IEpicorClient` para invocar Epicor Functions

**Files:**
- Modify: `src/OCAutomatica.Api/Epicor/IEpicorClient.cs`
- Modify: `src/OCAutomatica.Api/Epicor/EpicorClient.cs`
- Modify: `tests/OCAutomatica.Api.Tests/Epicor/EpicorClientTests.cs`
- Modify (solo agregar un método stub, sin lógica): `tests/OCAutomatica.Api.Tests/Vendors/VendorServiceTests.cs`, `tests/OCAutomatica.Api.Tests/Parts/PartServiceTests.cs`, `tests/OCAutomatica.Api.Tests/Buyers/BuyerServiceTests.cs`, `tests/OCAutomatica.Api.Tests/Auth/AuthServiceTests.cs`, `tests/OCAutomatica.Api.Tests/Organization/OrganizationServiceTests.cs`, `tests/OCAutomatica.Api.Tests/Integration/TestEpicorClient.cs`

**Por qué existe esta tarea:** Epicor Functions se invocan bajo `/api/v2/efx/{Company}/{Library}/{Function}/`, un segmento de URL distinto al `/api/v2/odata/` que usa todo el código de los Planes 1-2. `IEpicorClient` necesita un método nuevo para esto — no es una extensión de `PostAsync`, es una forma de armar la URL completamente distinta.

**Interfaces:**
- Consumes: `EpicorOptions.BaseUrl`, `EpicorOptions.ApiKey`, `EpicorCredentials`
- Produces: `IEpicorClient.InvokeFunctionAsync<T>(string company, string library, string function, object input, EpicorCredentials credentials, CancellationToken ct = default)`

- [ ] **Step 1: Escribir los tests que fallan**

Agregar a `tests/OCAutomatica.Api.Tests/Epicor/EpicorClientTests.cs` (dentro de la clase `EpicorClientTests` existente, junto a los tests de `GetAsync`):

```csharp
private sealed record FunctionOutput(int PONum);
private sealed record FunctionInput(string Plant, string VendorID);

[Fact]
public async Task InvokeFunctionAsync_BuildsEfxUrl_NotOdata()
{
    var handler = new FakeHttpMessageHandler(
        HttpStatusCode.OK, """{"PONum":123456}""");
    var client = BuildClient(handler);

    await client.InvokeFunctionAsync<FunctionOutput>(
        "CFSJ_LAF",
        "OCA",
        "OCA_CrearOC",
        new FunctionInput("LAF", "001008"),
        new EpicorCredentials("user", "pass"));

    Assert.Equal(
        "https://epicor-test/erp102600v2/api/v2/efx/CFSJ_LAF/OCA/OCA_CrearOC/",
        handler.LastRequest!.RequestUri!.ToString());
}

[Fact]
public async Task InvokeFunctionAsync_UsesPostVerb()
{
    var handler = new FakeHttpMessageHandler(
        HttpStatusCode.OK, """{"PONum":123456}""");
    var client = BuildClient(handler);

    await client.InvokeFunctionAsync<FunctionOutput>(
        "CFSJ_LAF", "OCA", "OCA_CrearOC",
        new FunctionInput("LAF", "001008"),
        new EpicorCredentials("user", "pass"));

    Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
}

[Fact]
public async Task InvokeFunctionAsync_SendsBasicAuthAndApiKey()
{
    var handler = new FakeHttpMessageHandler(
        HttpStatusCode.OK, """{"PONum":123456}""");
    var client = BuildClient(handler);

    await client.InvokeFunctionAsync<FunctionOutput>(
        "CFSJ_LAF", "OCA", "OCA_CrearOC",
        new FunctionInput("LAF", "001008"),
        new EpicorCredentials("jyanez", "secreto"));

    var request = handler.LastRequest!;
    var expectedAuth = Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes("jyanez:secreto"));

    Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
    Assert.Equal(expectedAuth, request.Headers.Authorization.Parameter);
    Assert.Equal("test-api-key", request.Headers.GetValues("x-api-key").Single());
}

[Fact]
public async Task InvokeFunctionAsync_SendsInputAsJsonBody()
{
    var handler = new FakeHttpMessageHandler(
        HttpStatusCode.OK, """{"PONum":123456}""");
    var client = BuildClient(handler);

    await client.InvokeFunctionAsync<FunctionOutput>(
        "CFSJ_LAF", "OCA", "OCA_CrearOC",
        new FunctionInput("LAF", "001008"),
        new EpicorCredentials("user", "pass"));

    var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
    Assert.Contains("\"Plant\":\"LAF\"", body);
    Assert.Contains("\"VendorID\":\"001008\"", body);
}

[Fact]
public async Task InvokeFunctionAsync_DeserializesResponse()
{
    var handler = new FakeHttpMessageHandler(
        HttpStatusCode.OK, """{"PONum":123456}""");
    var client = BuildClient(handler);

    var result = await client.InvokeFunctionAsync<FunctionOutput>(
        "CFSJ_LAF", "OCA", "OCA_CrearOC",
        new FunctionInput("LAF", "001008"),
        new EpicorCredentials("user", "pass"));

    Assert.NotNull(result);
    Assert.Equal(123456, result!.PONum);
}

[Fact]
public async Task InvokeFunctionAsync_ThrowsEpicorExceptionOnBusinessRuleFailure()
{
    // Same exception shape as BO validation failures (Ice.Common.BusinessObjectException),
    // confirmed in the REST guide's error-mapping section — Functions reuse it too.
    var handler = new FakeHttpMessageHandler(HttpStatusCode.BadRequest,
        """{"HttpStatus":400,"ReasonPhrase":"REST Api Exception","ErrorMessage":"Part is required.","ErrorType":"Ice.Common.BusinessObjectException"}""");
    var client = BuildClient(handler);

    var ex = await Assert.ThrowsAsync<EpicorException>(() =>
        client.InvokeFunctionAsync<FunctionOutput>(
            "CFSJ_LAF", "OCA", "OCA_CrearOC",
            new FunctionInput("LAF", "001008"),
            new EpicorCredentials("user", "pass")));

    Assert.Equal(EpicorErrorReason.Other, ex.Reason);
    Assert.Equal("Part is required.", ex.Message);
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter EpicorClientTests`
Expected: FAIL — `InvokeFunctionAsync` no existe en `IEpicorClient`

- [ ] **Step 3: Extender la interfaz**

En `src/OCAutomatica.Api/Epicor/IEpicorClient.cs`, agregar dentro de la interfaz existente (no quitar nada):

```csharp
    Task<T?> InvokeFunctionAsync<T>(
        string company,
        string library,
        string function,
        object input,
        EpicorCredentials credentials,
        CancellationToken ct = default);
```

- [ ] **Step 4: Implementar en `EpicorClient`, refactorizando el armado de headers**

En `src/OCAutomatica.Api/Epicor/EpicorClient.cs`, reemplazar el método privado `BuildRequest` actual por este (extrae el armado de headers a un helper compartido, ya que `InvokeFunctionAsync` lo necesita pero construye una URL distinta):

```csharp
    private HttpRequestMessage BuildRequest(
        HttpMethod method,
        string company,
        string relativePath,
        EpicorCredentials credentials)
    {
        var url = $"{_options.BaseUrl}/api/v2/odata/{company}/{relativePath}";
        var request = new HttpRequestMessage(method, url);
        AddAuthHeaders(request, credentials);
        return request;
    }

    private void AddAuthHeaders(HttpRequestMessage request, EpicorCredentials credentials)
    {
        var token = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Add("x-api-key", _options.ApiKey);
    }

    public Task<T?> InvokeFunctionAsync<T>(
        string company,
        string library,
        string function,
        object input,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        // Epicor Functions live under /api/v2/efx/ — a completely separate URL
        // segment from the OData BO/BAQ surface under /api/v2/odata/ used by
        // every other method in this class. Confirmed in the Kinetic REST
        // Services Guide v2's "Invoking Epicor Functions" section.
        var url = $"{_options.BaseUrl}/api/v2/efx/{company}/{library}/{function}/";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        AddAuthHeaders(request, credentials);
        request.Content = JsonContent.Create(input);
        return SendAsync<T>(request, ct);
    }
```

Todo lo demás en el archivo (`GetAsync`, `PostAsync`, `SendAsync`, `BuildException`, `ClassifyReason`) queda igual — no dependen de este cambio.

- [ ] **Step 5: Correr los tests de `EpicorClientTests` para verificar que pasan**

Run: `dotnet test --filter EpicorClientTests`
Expected: todos los tests de `EpicorClientTests` pasan (los de `GetAsync` no deben romperse por el refactor de `BuildRequest`/`AddAuthHeaders`)

- [ ] **Step 6: Arreglar la compilación — agregar el stub a cada implementación de prueba de `IEpicorClient`**

En cada uno de estos 6 archivos existe una clase (privada `StubEpicorClient` en 5 de ellos, pública `TestEpicorClient` en el sexto) que implementa `IEpicorClient` directamente. Agregar este método a cada una — el cuerpo es idéntico en los 5 `StubEpicorClient` (ninguno de esos tests ejercita Functions):

```csharp
        public Task<T?> InvokeFunctionAsync<T>(
            string company, string library, string function, object input,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException(
                "InvokeFunctionAsync is not exercised by this test suite.");
```

Archivos a tocar:
- `tests/OCAutomatica.Api.Tests/Vendors/VendorServiceTests.cs` (clase `StubEpicorClient`)
- `tests/OCAutomatica.Api.Tests/Parts/PartServiceTests.cs` (clase `StubEpicorClient`)
- `tests/OCAutomatica.Api.Tests/Buyers/BuyerServiceTests.cs` (clase `StubEpicorClient`)
- `tests/OCAutomatica.Api.Tests/Auth/AuthServiceTests.cs` (clase `StubEpicorClient`)
- `tests/OCAutomatica.Api.Tests/Organization/OrganizationServiceTests.cs` (clase `StubEpicorClient`)
- `tests/OCAutomatica.Api.Tests/Integration/TestEpicorClient.cs` (clase pública `TestEpicorClient` — usa el mismo mensaje que ya tiene su `PostAsync`, por consistencia: `"InvokeFunctionAsync is not exercised by the endpoints covered by these integration tests."`)

- [ ] **Step 7: Verificar que todo compila y pasa**

Run: `dotnet test`
Expected: `Passed! - Failed: 0, Passed: 45` (39 existentes + 6 nuevos de `InvokeFunctionAsync`)

- [ ] **Step 8: Commit**

```bash
git add src/OCAutomatica.Api/Epicor tests/OCAutomatica.Api.Tests/Epicor/EpicorClientTests.cs tests/OCAutomatica.Api.Tests/Vendors/VendorServiceTests.cs tests/OCAutomatica.Api.Tests/Parts/PartServiceTests.cs tests/OCAutomatica.Api.Tests/Buyers/BuyerServiceTests.cs tests/OCAutomatica.Api.Tests/Auth/AuthServiceTests.cs tests/OCAutomatica.Api.Tests/Organization/OrganizationServiceTests.cs tests/OCAutomatica.Api.Tests/Integration/TestEpicorClient.cs
git commit -m "feat: add IEpicorClient.InvokeFunctionAsync for calling Epicor Functions via REST"
```

---

## Task 2: Publicar el BAQ `OCA_CambiosFisicos` en Epicor

**Este es un paso manual en Epicor, no una tarea de código.** No la despaches a un subagente implementador.

**Por qué existe esta tarea:** hoy el comprador no ve los Cambios Físicos pendientes hasta que salen impresos en la OC. Este BAQ los muestra en la pantalla *antes* de procesar (spec, sección 6-7). Es un consumidor **distinto e independiente** de la Function del Task 3, que hace su propia consulta al construir el comentario — no dependen entre sí.

### 2.1 — Cómo entrar (BAQ Designer, igual que en el Plan 2)

Menú de Epicor: **Business Activity Queries > BAQ Designer**. Nuevo BAQ con ID **`OCA_CambiosFisicos`**. El flujo es idéntico al que ya usaste para `OCA_PartesPorProveedor` en el Plan 2 — Query tab para tablas/joins/criteria, Calculated Fields para el agregado, Display Fields para lo que se expone.

### 2.2 — Tabla base, join, filtro y agrupación

- Tabla base: **`UD104A`** (alias `UD104A`).
- Join: **Inner Join** con **`UD104`** (alias `UD104`) on `UD104A.Company = UD104.Company` AND `UD104A.Key1 = UD104.Key1`.
- Criteria: `UD104A.Character06 = 'PENDIENTE'` AND `UD104A.Character10 = vendorId` (parámetro) AND `UD104.ShortChar01 = CurrentPlant` (parámetro) — igual que en el Plan 2, no declares `CurrentCompany` como parámetro salvo que la verificación real muestre que hace falta (ahí no hizo falta).
- Group By: `UD104A.Character01`, `UD104A.Character02`, `UD104A.Character04`, `UD104A.Character06` (el query legacy también agrupa por `Character03` sin mostrarlo — si el BAQ Designer te exige incluirlo en el Group By por estar en la tabla base, agrégalo, pero no lo pongas en Display Fields).
- Calculated Field (agregado, sin Group By marcado): `Sum(UD104A.Number01)` — alias `CantidadPendiente`.

### 2.3 — Campos a mostrar

| Campo | Origen |
|---|---|
| `Character01` | `UD104A.Character01` |
| `Character02` | `UD104A.Character02` |
| `Character04` | `UD104A.Character04` |
| `CantidadPendiente` | Calculated field `Sum(UD104A.Number01)` |
| `Character06` | `UD104A.Character06` |

No renombres estos campos — el DTO de la Task 4 los usa tal cual, igual que el Plan 2 usó los nombres reales de Epicor sin traducirlos.

### 2.4 — Publicar y dar acceso

Igual que en el Plan 2: publicar el BAQ, exponerlo por REST, y confirmar que el Access Scope de la API key ya usada lo incluye (probablemente sí, si el scope no es por-BAQ sino general — verificar con una llamada real).

### 2.5 — Verificación real (pendiente hasta que se construya)

Una vez publicado, ejecuta (ajustando el endpoint/parámetros a los reales que resulten):

```
GET /api/v2/odata/CFSJ_LAF/BaqSvc/OCA_CambiosFisicos/Data?CurrentPlant=LAF&vendorId=001008
```

Reporta el resultado (o pide que lo verifique yo por REST) antes de dar por completa la Task 4, que depende de los nombres reales de campo/parámetro de este BAQ.

---

## Task 3: Publicar la Function `OCACrearOC` en Epicor

**Este es un paso manual en Epicor (Epicor Functions Maintenance), no una tarea de código.** No la despaches a un subagente implementador — requiere una interfaz gráfica que ningún subagente tiene.

**Por qué existe esta tarea:** reemplaza `btnProc_Click`, trasladando la secuencia completa de creación de la OC a una sola transacción del lado de Epicor (defecto 10.3), eliminando el `BuyerID` fijo (defecto 10.4, ya resuelto río arriba: la API se lo pasa ya calculado) y la fuga de conexión SQL directa (defecto 10.5: la Function consulta Cambios Físicos con una BO/BAQ interna de Epicor, nunca con una `SqlConnection`).

**Actualización — cambio de enfoque confirmado:** este plan originalmente recomendaba "Widget Function with Code" (GUI + widgets). Se cambió a **Custom Code Function** (C# libre) tras confirmar que sí es viable: el usuario ya tiene un ejemplo propio, funcionando en producción, que invoca `POSvc` desde código libre con `this.CallService<Erp.Contracts.POSvcContract>(BO => { ... })`. Esto hace innecesaria la pelea con widgets — se escribe C# directo, más parecido a lo que ya se conoce de `POAdapter`. La librería y la Function ya se crearon con el nombre **`OCACrearOC`** (sin guion bajo, mismo nombre para ambas) — el código de la Task 5 usa ese nombre real, no `OCA`/`OCA_CrearOC` como se planeaba originalmente.

**Además, se verificaron por REST contra el ambiente de pruebas real las firmas de los 9 métodos de `POSvc` usados aquí** (nombres de parámetros confirmados llamando cada método vacío y leyendo el error "Parameter X is not found", luego completando la cadena real hasta crear una OC de prueba sin aprobar en `CFSJ_LAF`, PONum **3218**, comentario "*** PRUEBA TECNICA... IGNORAR / BORRAR ***" — puedes borrarla o dejarla, no está aprobada ni afecta nada). Y se encontró una segunda referencia real: un proyecto hermano (`Portal Porveedores/server/src/services/epicor.ts`) que ya crea OCs aprobadas por REST puro en producción, con lecciones muy valiosas que el código legacy y el manual no explican — incorporadas abajo.

### 3.1 — Librería y Function (ya creadas)

Ya hecho: librería **`OCACrearOC`**, Function **`OCACrearOC`** dentro de ella, tipo **Custom Code**, con **"Custom Code Functions"** marcado (no "Custom Code Widgets" — ese es para el otro tipo). Falta:

1. En **DB Access from Code**, cambia de **None** a **Read Only** — sin esto, el código no puede leer `UD104A`/`UD104` para los Cambios Físicos (ver 3.2). "Custom Code Functions" ya activó este campo, solo falta elegir la opción.
2. Pestaña **Security** → agrega `CFSJ_LAF` a **Authorized Companies** — si al probar por REST recibes 404, este es el primer lugar a revisar.

### 3.2 — Referencias de la librería (Library References)

Pestaña **References**:

- **Services**: agrega `Erp.BO.POSvc` — expone el contrato `Erp.Contracts.POSvcContract` que el código usa vía `this.CallService<Erp.Contracts.POSvcContract>(...)`.
- **Tables**: agrega `UD104A` y `UD104`, ambas solo lectura (no marques "Updatable").

### 3.3 — Parámetros de entrada y salida

Pestaña **Signature**. Entrada (Direction = In):

| Parámetro | Tipo | Descripción |
|---|---|---|
| `Plant` | string | Planta activa de la sesión |
| `VendorID` | string | Proveedor seleccionado |
| `BuyerID` | string | Ya resuelto por la API (Plan 1) — la Function **no** lo calcula, solo lo usa |
| `Comentarios` | string | Texto libre del comprador — **sin** el bloque de Cambios Físicos, que el código arma por su cuenta |
| `Lineas` | Tabla (tableset), columnas `PartNum` (string), `Cantidad` (decimal), `Costo` (decimal), `UOM` (string) | Artículos marcados, ya decimales (Plan 2) |

Salida (Direction = Out):

| Parámetro | Tipo | Descripción |
|---|---|---|
| `PONum` | int | Número de la orden creada |

**Nota sobre nombres:** tu propio ejemplo usa el prefijo `ip`/`op` (`ipPONum`, `opMessage`) — es un estilo válido, no un requisito de Epicor. Aquí se usan nombres limpios (`Plant`, `VendorID`...) porque deben coincidir **exactamente** con las propiedades del DTO que ya arma el backend en la Task 5 (`PurchaseOrderFunctionInput`/`PurchaseOrderFunctionOutput`) — Epicor hace match exacto de nombre de parámetro contra la propiedad JSON del body (confirmado por REST: `poNUM` funciona, `poNum` no). Si prefieres el prefijo `ip`/`op`, úsalo, pero entonces avísame para actualizar el DTO de la Task 5 con los mismos nombres.

Cualquier fallo (parte bloqueada, error de Epicor, etc.) debe **detener la ejecución lanzando `BLException`** con un mensaje descriptivo — nunca dejar `PONum` en 0 silenciosamente. Eso es lo que el backend (Task 5) recibe como `EpicorException` con el mensaje real.

### 3.4 — El código

Pegar en el editor de la Function (botón **Edit**). **Nota sobre el orden exacto de parámetros:** los *nombres* de cada parámetro de `POSvc` abajo están confirmados por REST contra el ambiente real (ver arriba) — pero el *orden* dentro de cada llamada C# (y si es `ref`/`out`/valor) es mi mejor inferencia a partir de tus propios ejemplos, no algo que pude verificar 1:1 desde REST. Usa **Ctrl+Espacio** después de escribir `BO.` para que el editor te muestre la firma real y ajusta el orden si el compilador se queja — es rápido y elimina la única incertidumbre real que queda.

```csharp
try
{
    // ===== FASE 1: crear el encabezado SIN aprobar, guardarlo para obtener el PONum real =====
    // No aprobar aqui todavia: si una linea falla mas adelante, la orden quedaria
    // aprobada/bloqueada en Epicor y el siguiente intento fallaria con
    // "is approved, cannot update" (leccion real tomada de una integracion previa
    // con este mismo patron de llamadas - Portal Proveedores/server/src/services/epicor.ts).
    Erp.Tablesets.POTableset ds = new Erp.Tablesets.POTableset();
    this.CallService<Erp.Contracts.POSvcContract>(BO => {
        BO.GetNewPOHeader(ref ds);
    });

    this.CallService<Erp.Contracts.POSvcContract>(BO => {
        BO.ChangeVendor(VendorID, ref ds);
    });

    var header = ds.POHeader[0];
    header.BuyerID = BuyerID; // defecto 10.4: siempre el que llega, ya validado por la API antes de llamar aqui

    // Cambios Fisicos pendientes - consulta propia, independiente del BAQ de la interfaz
    // (Task 2): el comentario debe reflejar el estado en el momento exacto de crear la
    // orden, no lo que el comprador llego a ver antes. DB Context en vez de SqlConnection
    // directa (defecto 10.5).
    var cambiosRaw = this.Db.UD104A
        .Where(a => a.Character06 == "PENDIENTE" && a.Character10 == VendorID)
        .Join(this.Db.UD104, a => new { a.Company, a.Key1 }, u => new { u.Company, u.Key1 }, (a, u) => new { a, u })
        .Where(x => x.u.ShortChar01 == Plant)
        .Select(x => x.a)
        .ToList();

    string cambiosTexto = string.Join(" - ", cambiosRaw
        .GroupBy(a => new { a.Character01, a.Character02, a.Character04, a.Character06 })
        .Select(g => string.Format("{0}  {1}  {2}  {3:0.00}  {4}",
            g.Key.Character01, g.Key.Character02, g.Key.Character04,
            g.Sum(a => a.Number01), g.Key.Character06)));

    // Una sola construccion del comentario, en un solo lugar - corrige el defecto 10.6
    // (el legacy lo asignaba aqui y lo sobrescribia mas adelante con otro metodo).
    header.CommentText = string.IsNullOrEmpty(cambiosTexto)
        ? Comentarios
        : string.Format("{0}\r\n\r\nCambios Fisicos:  {1}", Comentarios, cambiosTexto);

    header.RowMod = "A";

    this.CallService<Erp.Contracts.POSvcContract>(BO => {
        BO.Update(ref ds);
    });

    int poNum = ds.POHeader[0].PONum;
    if (poNum <= 0)
    {
        throw new BLException("Epicor no asigno un numero de orden de compra.");
    }

    // ===== FASE 2: agregar cada linea, una a la vez, con su propio Update =====
    Erp.Tablesets.POTableset currentDs = ds;
    foreach (var linea in Lineas)
    {
        Erp.Tablesets.POTableset lineDs = currentDs;
        this.CallService<Erp.Contracts.POSvcContract>(BO => {
            BO.GetNewPODetail(ref lineDs, poNum);
        });

        // La ranura nueva tiene RowMod='A' y PartNum vacio; con fallback al ultimo renglon
        // (mismo patron confirmado en Portal Proveedores/epicor.ts).
        var detalle = lineDs.PODetail.FirstOrDefault(d => d.RowMod == "A" && string.IsNullOrEmpty(d.PartNum))
                      ?? lineDs.PODetail.LastOrDefault();
        if (detalle == null)
        {
            throw new BLException("No se pudo agregar la linea " + linea.PartNum);
        }

        string partNum = linea.PartNum;
        string questionString, msgType;
        bool substitutePartAvail;

        // A diferencia del legacy, que llamaba esto y descartaba el resultado sin revisarlo
        // (hallazgo nuevo, no listado en los 8 defectos originales del spec), aqui si se
        // revisa msgType y se detiene la transaccion si indica un bloqueo.
        this.CallService<Erp.Contracts.POSvcContract>(BO => {
            BO.PartStatusValidationMessages(ref partNum, out questionString, out substitutePartAvail, out msgType);
        });
        if (!string.IsNullOrEmpty(msgType))
        {
            throw new BLException(string.Format("La parte {0} no se puede procesar: {1}", linea.PartNum, questionString));
        }

        Guid sysRow = detalle.SysRowID;
        bool multipleMatch;
        this.CallService<Erp.Contracts.POSvcContract>(BO => {
            BO.ChangeDetailPartNum(ref partNum, sysRow, "", false, out multipleMatch, ref lineDs);
        });

        detalle = lineDs.PODetail.FirstOrDefault(d => d.RowMod == "A" || d.RowMod == "U");
        detalle.PartNum = linea.PartNum;
        detalle.PUM = linea.UOM;
        detalle.CurrencySwitch = false;

        this.CallService<Erp.Contracts.POSvcContract>(BO => {
            BO.ChangeDetailCalcOurQty(linea.Cantidad, ref lineDs);
        });

        detalle = lineDs.PODetail.FirstOrDefault(d => d.PartNum == linea.PartNum && (d.RowMod == "A" || d.RowMod == "U"))
                  ?? lineDs.PODetail.LastOrDefault();
        detalle.CalcOurQty = linea.Cantidad;

        // Epicor ignora UnitCost/DocUnitCost puestos directamente - el campo de pantalla
        // real es DocScrUnitCost/ScrUnitCost. ChangeUnitPrice lee ESE campo y recalcula
        // UnitCost, DocUnitCost, totales e impuestos por su cuenta (leccion real tomada de
        // Portal Proveedores/epicor.ts - sin esto, el precio queda en cero silenciosamente).
        detalle.DocScrUnitCost = linea.Costo;
        detalle.ScrUnitCost = linea.Costo;

        string confirmMsg;
        this.CallService<Erp.Contracts.POSvcContract>(BO => {
            BO.ChangeUnitPriceConfirmOverride(out confirmMsg, ref lineDs);
        });
        this.CallService<Erp.Contracts.POSvcContract>(BO => {
            BO.ChangeUnitPrice(ref lineDs);
        });

        // Filtra el dataset antes de guardar - Epicor puede devolver renglones de otra
        // orden dentro del mismo dataset entre llamadas sin estado; si se mandan tal cual
        // al Update, falla con "is approved, cannot update" sobre la orden equivocada
        // (mismo hallazgo real de Portal Proveedores/epicor.ts).
        var safeDs = new Erp.Tablesets.POTableset
        {
            POHeader = lineDs.POHeader.Where(h => h.PONum == poNum).ToList(),
            PODetail = lineDs.PODetail.Where(d => d.PONum == poNum || d.RowMod == "A" || d.RowMod == "U").ToList()
        };

        this.CallService<Erp.Contracts.POSvcContract>(BO => {
            BO.Update(ref safeDs);
        });
        currentDs = safeDs;
    }

    // ===== FASE 3: aprobar solo hasta que TODAS las lineas quedaron guardadas =====
    // Aprobar antes deja la orden bloqueada si una linea posterior falla (ver nota Fase 1).
    var finalHeader = currentDs.POHeader.FirstOrDefault(h => h.PONum == poNum) ?? currentDs.POHeader[0];
    finalHeader.Approve = true;
    finalHeader.ApprovalStatus = "A";
    finalHeader.Unlock_c = true;
    finalHeader.RowMod = "U";

    var approveDs = new Erp.Tablesets.POTableset { POHeader = new List<Erp.Tablesets.POHeaderRow> { finalHeader } };
    string violationMsg;
    this.CallService<Erp.Contracts.POSvcContract>(BO => {
        BO.ChangeApproveSwitch(true, out violationMsg, ref approveDs);
    });
    this.CallService<Erp.Contracts.POSvcContract>(BO => {
        BO.Update(ref approveDs);
    });

    PONum = poNum;
}
catch (Exception ex)
{
    throw new BLException("Error al crear la orden de compra: " + ex.Message);
}
```

### 3.5 — Verificación real (parcialmente ya hecha)

**Ya confirmado por REST, llamada por llamada, sin aprobar nada:** `GetNewPOHeader(ds)`, `ChangeVendor(VendID, ds)` (resuelve `VendorNum` correctamente — probado con `001008` → `1676`), `Update(ds)` (asigna `PONum` real — probado, PO **3218** creado sin aprobar), `GetNewPODetail(ds, poNUM)` (nota el casing exacto: `poNUM`, no `poNum`), `PartStatusValidationMessages(valpartnum)` (dataset-independiente, sin `ds`), `ChangeDetailPartNum(NewPartNum, SysRowID, rowType, isSubstitute, ds)`, `ChangeDetailCalcOurQty(newCalcOurQty, ds)`, `ChangeUnitPriceConfirmOverride(ds)`, `ChangeUnitPrice(ds)` (confirmado que actualiza `DocUnitCost` a partir de `DocScrUnitCost`), `ChangeApproveSwitch(ApproveValue, ds)`. Todos los nombres de parámetro reales, no adivinados.

**Falta verificar, una vez que pegues el código:** que compile con `Check Syntax`, y una corrida completa de punta a punta creando una OC real **aprobada** (puedes usar el mismo proveedor `001008`/parte `8310400932` que ya se usó en las pruebas anteriores). Confirma que: (a) `PONum` regresa un número real, (b) la OC en Epicor aparece **aprobada**, con el `BuyerID` correcto y el comentario combinado una sola vez, (c) una línea con un `PartNum` inválido detiene todo con un mensaje claro, sin dejar la OC a medias.

---

## Task 4: Backend — Cambios Físicos (vista previa)

**Files:**
- Create: `src/OCAutomatica.Api/PurchaseOrders/ICambiosFisicosService.cs`
- Create: `src/OCAutomatica.Api/PurchaseOrders/CambiosFisicosService.cs`
- Create: `src/OCAutomatica.Api/Controllers/CambiosFisicosController.cs`
- Modify: `src/OCAutomatica.Api/Program.cs`
- Test: `tests/OCAutomatica.Api.Tests/PurchaseOrders/CambiosFisicosServiceTests.cs`

**Precondición cumplida:** la Task 2 ya está publicada y verificada por REST. Resultado real (`ctx_execute` contra `CFSJ_LAF`, endpoint `BaqSvc/OCA_CambiosFisicos/Data?CurrentPlant=LAF&vendorId=001008` → HTTP 200, forma correcta; no se encontró un proveedor con filas `PENDIENTE` reales en el ambiente de pruebas para confirmar el agregado con datos positivos, pero la estructura del BAQ coincide campo por campo con el query legacy). **Los nombres reales de campo llevan el prefijo de tabla/calculated, igual que en el Plan 2** — no son los nombres limpios que asumía el borrador original de esta sección:

| Campo origen | Nombre real en la respuesta REST |
|---|---|
| `UD104A.Character01` | `UD104A_Character01` |
| `UD104A.Character02` | `UD104A_Character02` |
| `UD104A.Character04` | `UD104A_Character04` |
| `Sum(UD104A.Number01)` | `Calculated_CantidadPendiente` |
| `UD104A.Character06` | `UD104A_Character06` |

**Interfaces:**
- Consumes: `IEpicorClient.GetAsync<T>`, `EpicorCredentials`, `EpicorException`, `ISessionStore`, `UserSession`, `SessionMiddleware.ItemKey`
- Produces:
  - `record CambioFisico(string Character01, string Character02, string Character04, decimal CantidadPendiente, string Character06)` — el modelo de dominio, limpio, expuesto al frontend — campos crudos del BAQ sin nombres inventados: el legacy nunca les da significado semántico, solo los concatena para mostrarlos (ver "Query real de Cambios Físicos" al inicio del plan)
  - Un DTO interno de deserialización (`CambioFisicoDto`, siguiendo el mismo patrón que `PartDto` del Plan 2) con las 5 propiedades reales de la tabla — `UD104A_Character01`, `UD104A_Character02`, `UD104A_Character04`, `Calculated_CantidadPendiente`, `UD104A_Character06` — que el servicio mapea 1:1 a `CambioFisico` (sin renombrar el significado, solo quitando el prefijo de transporte)
  - `interface ICambiosFisicosService { Task<IReadOnlyList<CambioFisico>> GetPendingAsync(string company, string plant, string vendorId, EpicorCredentials credentials, CancellationToken ct = default) }`
  - `GET /api/cambios-fisicos?vendorId={id}` → `CambioFisico[]`

Sigue exactamente el patrón de `IPartService`/`PartService`/`PartsController` (Plan 2, Task 3) — mismo tipo de servicio BAQ-backed, mismo manejo de sesión/errores. Escribe:

1. Tests (`GetPendingAsync_MapsEpicorResponse`, `GetPendingAsync_ReturnsEmptyWhenNoResponse`, `GetPendingAsync_PassesPlantAndVendorAsBaqParameters`) usando el mismo `StubEpicorClient` inline que ya usan `VendorServiceTests`/`PartServiceTests` (con el stub de `InvokeFunctionAsync` del Task 1 agregado). El fixture de `GetPendingAsync_MapsEpicorResponse` debe construir el DTO con los 5 nombres reales de la tabla anterior, no con nombres limpios.
2. `ICambiosFisicosService`/`CambiosFisicosService` — `Uri.EscapeDataString` en `plant`/`vendorId`, endpoint `BaqSvc/OCA_CambiosFisicos/Data`, parámetros `CurrentPlant`/`vendorId` (confirmados reales, sin `CurrentCompany`).
3. `CambiosFisicosController` — mismo esqueleto que `PartsController`/`VendorsController`: sesión → credenciales → `try/catch (EpicorException)` → `HandleEpicorException` con mensajes en español ("Tu usuario de Epicor no tiene permiso para consultar Cambios Fisicos...", etc.). Si `vendorId` viene vacío, `Ok(Array.Empty<CambioFisico>())` (no es un error, simplemente no hay proveedor seleccionado aún).
4. Registrar `ICambiosFisicosService` en `Program.cs`.
5. Commit.

**Nota:** esta vista previa es informativa para el comprador — no necesita reproducir carácter por carácter el texto que termina en el comentario de la OC (eso lo construye la Function, de forma independiente, en la Task 3). Alcanza con mostrar los campos de forma legible.

- [ ] Escrito, probado y commiteado siguiendo los 5 puntos anteriores (usa el mismo ciclo TDD rojo→verde→commit de las tareas previas).

---

## Task 5: Backend — Creación de la orden de compra

**Files:**
- Create: `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderModels.cs`
- Create: `src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderService.cs`
- Create: `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderService.cs`
- Create: `src/OCAutomatica.Api/Controllers/PurchaseOrdersController.cs`
- Modify: `src/OCAutomatica.Api/Program.cs`
- Test: `tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderServiceTests.cs`

**Precondición:** la Task 3 debe estar publicada y verificada por REST — el nombre de librería (`OCA` en este borrador) y los nombres de parámetro deben coincidir con lo que se haya confirmado ahí.

**Interfaces:**
- Consumes: `IEpicorClient.InvokeFunctionAsync<T>` (Task 1), `EpicorCredentials`, `EpicorException`, `EpicorErrorReason`, `ISessionStore`, `UserSession` (incluyendo `session.BuyerId`), `SessionMiddleware.ItemKey`
- Produces:
  - `record PurchaseOrderLine(string PartNum, decimal Cantidad, decimal Costo, string Uom)`
  - `record CreatePurchaseOrderRequest(string VendorId, string Comentarios, IReadOnlyList<PurchaseOrderLine> Lineas)`
  - `record CreatePurchaseOrderResult(int PoNum)`
  - `interface IPurchaseOrderService { Task<CreatePurchaseOrderResult> CreateAsync(string company, string plant, string buyerId, CreatePurchaseOrderRequest request, EpicorCredentials credentials, CancellationToken ct = default) }`
  - `POST /api/purchase-orders` → `{ poNum: number }`

**Nota de diseño — decimales (defecto 10.1):** `Cantidad`/`Costo` son `decimal` de punta a punta, igual que en `PartRow` del Plan 2. Nunca `int`/`double`.

**Nota de diseño — mensaje de error real (requisito del spec, sección 6: "Salida: `poNum`, o error descriptivo"):** a diferencia de `VendorsController`/`PartsController`, cuando `ex.Reason == EpicorErrorReason.Other` este controlador **no** debe mostrar un mensaje genérico — debe mostrar `ex.Message` tal cual, porque ahí es donde llegan los rechazos de negocio de la propia Function (parte bloqueada, línea inválida, etc.), que el comprador necesita leer para corregir y reintentar.

**Nota de diseño — bloqueo sin buyer (defecto 10.4 / criterio de aceptación 10):** el controlador valida `session.BuyerId` **antes** de llamar al servicio. Si es null/vacío, responde 403 con un mensaje claro — nunca inventa ni asume un buyer. `session.BuyerId` ya viene resuelto por el Plan 1; esta tarea no vuelve a resolverlo.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderServiceTests.cs`:

```csharp
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.PurchaseOrders;

namespace OCAutomatica.Api.Tests.PurchaseOrders;

public class PurchaseOrderServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly object? _response;
        public string? LastCompany { get; private set; }
        public string? LastLibrary { get; private set; }
        public string? LastFunction { get; private set; }
        public object? LastInput { get; private set; }

        public StubEpicorClient(object? response) => _response = response;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by PurchaseOrderService.");

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("Not used by PurchaseOrderService.");

        public Task<T?> InvokeFunctionAsync<T>(string company, string library, string function,
            object input, EpicorCredentials credentials, CancellationToken ct = default)
        {
            LastCompany = company;
            LastLibrary = library;
            LastFunction = function;
            LastInput = input;
            return Task.FromResult((T?)_response);
        }
    }

    private static readonly EpicorCredentials Creds = new("jyanez", "x");

    private static CreatePurchaseOrderRequest Request(params PurchaseOrderLine[] lines) =>
        new("001008", "prueba", lines.ToList());

    [Fact]
    public async Task CreateAsync_InvokesTheOCACrearOCFunction()
    {
        var client = new StubEpicorClient(new { PONum = 123456 });
        var service = new PurchaseOrderService(client);

        var result = await service.CreateAsync(
            "CFSJ_LAF", "LAF", "LAF-CM5",
            Request(new PurchaseOrderLine("8310400932", 5.5m, 53m, "KGS")),
            Creds);

        Assert.Equal(123456, result.PoNum);
        Assert.Equal("CFSJ_LAF", client.LastCompany);
        Assert.Equal("OCACrearOC", client.LastLibrary);
        Assert.Equal("OCACrearOC", client.LastFunction);
    }

    [Fact]
    public async Task CreateAsync_PreservesDecimalPrecisionInLines()
    {
        // Guards the same class of bug fixed in Plan 2's PartRow: quantities
        // and costs must survive as decimal, never truncated en route to the
        // Function's Lineas input.
        var client = new StubEpicorClient(new { PONum = 1 });
        var service = new PurchaseOrderService(client);

        await service.CreateAsync(
            "CFSJ_LAF", "LAF", "LAF-CM5",
            Request(new PurchaseOrderLine("X", 0.5m, 12.345m, "KGS")),
            Creds);

        var input = Assert.IsType<PurchaseOrderFunctionInput>(client.LastInput);
        Assert.Equal(0.5m, input.Lineas[0].Cantidad);
        Assert.Equal(12.345m, input.Lineas[0].Costo);
    }

    [Fact]
    public async Task CreateAsync_SendsBuyerIdAndPlantAsReceived()
    {
        var client = new StubEpicorClient(new { PONum = 1 });
        var service = new PurchaseOrderService(client);

        await service.CreateAsync(
            "CFSJ_LAF", "LAF", "LAF-CM5",
            Request(new PurchaseOrderLine("X", 1m, 1m, "KGS")),
            Creds);

        var input = Assert.IsType<PurchaseOrderFunctionInput>(client.LastInput);
        Assert.Equal("LAF", input.Plant);
        Assert.Equal("LAF-CM5", input.BuyerID);
        Assert.Equal("001008", input.VendorID);
        Assert.Equal("prueba", input.Comentarios);
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter PurchaseOrderServiceTests`
Expected: FAIL — `PurchaseOrderService`, `PurchaseOrderFunctionInput` no existen

- [ ] **Step 3: Implementar los modelos**

Crear `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderModels.cs`:

```csharp
namespace OCAutomatica.Api.PurchaseOrders;

public sealed record PurchaseOrderLine(string PartNum, decimal Cantidad, decimal Costo, string Uom);

public sealed record CreatePurchaseOrderRequest(
    string VendorId,
    string Comentarios,
    IReadOnlyList<PurchaseOrderLine> Lineas);

public sealed record CreatePurchaseOrderResult(int PoNum);

// Shape of the JSON body sent to OCACrearOC — property names must match the
// Function's input parameter names exactly (Task 3, section 3.2).
public sealed class PurchaseOrderFunctionInput
{
    public string Plant { get; set; } = string.Empty;
    public string VendorID { get; set; } = string.Empty;
    public string BuyerID { get; set; } = string.Empty;
    public string Comentarios { get; set; } = string.Empty;
    public List<PurchaseOrderLineInput> Lineas { get; set; } = new();
}

public sealed class PurchaseOrderLineInput
{
    public string PartNum { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal Costo { get; set; }
    public string UOM { get; set; } = string.Empty;
}

// Shape of the JSON response from OCACrearOC (Task 3, section 3.3).
public sealed class PurchaseOrderFunctionOutput
{
    public int PONum { get; set; }
}
```

- [ ] **Step 4: Implementar la interfaz**

Crear `src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.PurchaseOrders;

public interface IPurchaseOrderService
{
    Task<CreatePurchaseOrderResult> CreateAsync(
        string company,
        string plant,
        string buyerId,
        CreatePurchaseOrderRequest request,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
```

- [ ] **Step 5: Implementar `PurchaseOrderService`**

Crear `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.PurchaseOrders;

public sealed class PurchaseOrderService : IPurchaseOrderService
{
    private readonly IEpicorClient _epicor;

    public PurchaseOrderService(IEpicorClient epicor) => _epicor = epicor;

    public async Task<CreatePurchaseOrderResult> CreateAsync(
        string company,
        string plant,
        string buyerId,
        CreatePurchaseOrderRequest request,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var input = new PurchaseOrderFunctionInput
        {
            Plant = plant,
            VendorID = request.VendorId,
            BuyerID = buyerId,
            Comentarios = request.Comentarios,
            Lineas = request.Lineas
                .Select(l => new PurchaseOrderLineInput
                {
                    PartNum = l.PartNum,
                    Cantidad = l.Cantidad,
                    Costo = l.Costo,
                    UOM = l.Uom
                })
                .ToList()
        };

        var response = await _epicor.InvokeFunctionAsync<PurchaseOrderFunctionOutput>(
            company, "OCACrearOC", "OCACrearOC", input, credentials, ct);

        if (response is null)
        {
            throw new EpicorException(
                500, EpicorErrorReason.Other, "Epicor no devolvio un numero de orden.");
        }

        return new CreatePurchaseOrderResult(response.PONum);
    }
}
```

- [ ] **Step 6: Correr los tests para verificar que pasan**

Run: `dotnet test --filter PurchaseOrderServiceTests`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 7: Escribir el controlador**

Crear `src/OCAutomatica.Api/Controllers/PurchaseOrdersController.cs`:

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
    private readonly ISessionStore _sessions;
    private readonly ILogger<PurchaseOrdersController> _logger;

    public PurchaseOrdersController(
        IPurchaseOrderService purchaseOrders,
        ISessionStore sessions,
        ILogger<PurchaseOrdersController> logger)
    {
        _purchaseOrders = purchaseOrders;
        _sessions = sessions;
        _logger = logger;
    }

    public sealed record CreatePurchaseOrderApiRequest(
        string VendorId, string? Comentarios, List<PurchaseOrderLine>? Lineas);

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreatePurchaseOrderApiRequest request, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(session.BuyerId))
        {
            return StatusCode(403, new
            {
                message = "No tienes un comprador asignado en esta compania. No puedes crear ordenes de compra."
            });
        }

        if (string.IsNullOrWhiteSpace(request.VendorId))
            return BadRequest(new { message = "Es necesario indicar un proveedor." });

        if (request.Lineas is null || request.Lineas.Count == 0)
            return BadRequest(new { message = "Es necesario marcar al menos un articulo con cantidad a surtir." });

        try
        {
            var result = await _purchaseOrders.CreateAsync(
                session.Company,
                session.Plant,
                session.BuyerId,
                new CreatePurchaseOrderRequest(request.VendorId, request.Comentarios ?? string.Empty, request.Lineas),
                credentials,
                ct);

            return Ok(new { poNum = result.PoNum });
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while creating a purchase order for vendor {VendorId} on {Company}",
                ex.Reason, request.VendorId, session.Company);
            return HandleEpicorException(ex);
        }
    }

    private IActionResult HandleEpicorException(EpicorException ex)
    {
        switch (ex.Reason)
        {
            case EpicorErrorReason.InvalidApiKey:
                return StatusCode(503, new
                {
                    message = "La aplicacion no pudo autenticarse con Epicor (clave de API invalida). Avisa a sistemas."
                });
            case EpicorErrorReason.AccessDenied:
                return StatusCode(503, new
                {
                    message = "Tu usuario de Epicor no tiene permiso para crear ordenes de compra. Pide que revisen tu perfil de seguridad en Epicor."
                });
            default:
                // "Other" covers both real infrastructure failures and
                // OCACrearOC's own business-rule rejections (blocked part,
                // invalid line, etc.). The spec requires the buyer see the
                // actual descriptive error from the Function here, not a
                // generic message, so the error's own text is passed through.
                return StatusCode(422, new { message = ex.Message });
        }
    }
}
```

- [ ] **Step 8: Registrar el servicio en `Program.cs`**

Agregar el `using`:

```csharp
using OCAutomatica.Api.PurchaseOrders;
```

Y junto a los demás registros:

```csharp
builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
```

- [ ] **Step 9: Verificar que todo compila y pasa**

Run: `dotnet test`
Expected: todos los tests pasan (los de Task 1 + Task 4 + estos 3 nuevos)

- [ ] **Step 10: Commit**

```bash
git add src/OCAutomatica.Api/PurchaseOrders src/OCAutomatica.Api/Controllers/PurchaseOrdersController.cs src/OCAutomatica.Api/Program.cs tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderServiceTests.cs
git commit -m "feat: add purchase order creation via the OCACrearOC Epicor Function"
```

---

## Task 6: Frontend — cliente de API y `PartsGrid` expone sus filas

**Files:**
- Modify: `src/web/src/api/client.ts`
- Modify: `src/web/src/parts/PartsGrid.tsx`

**Por qué existe esta tarea:** hoy `PartsGrid` (Plan 2) es autocontenido — nadie fuera del componente sabe qué filas están marcadas ni con qué cantidad. Para poder mandar `Lineas` a `OCACrearOC` desde un botón "Procesar" que vive en otro componente (Task 7), `PartsGrid` necesita avisar hacia afuera cada vez que sus filas cambian.

**Interfaces:**
- Produces:
  - `interface PurchaseOrderLine { partNum: string; cantidad: number; costo: number; uom: string }`
  - `interface CreatePurchaseOrderResult { poNum: number }`
  - `interface CambioFisico { character01: string; character02: string; character04: string; cantidadPendiente: number; character06: string }`
  - `api.purchaseOrders.create(vendorId: string, comentarios: string, lineas: PurchaseOrderLine[]): Promise<CreatePurchaseOrderResult>`
  - `api.cambiosFisicos.byVendor(vendorId: string): Promise<CambioFisico[]>`
  - `PartsGrid` gana la prop opcional `onRowsChange?: (rows: PartRowState[]) => void`
  - `isRowInvalid` (hoy privada de `PartsGrid.tsx`) se exporta

- [ ] **Step 1: Extender `client.ts`**

Agregar a `src/web/src/api/client.ts` (junto a `Vendor`/`PartRow`):

```typescript
export interface PurchaseOrderLine {
  partNum: string
  cantidad: number
  costo: number
  uom: string
}

export interface CreatePurchaseOrderResult {
  poNum: number
}

export interface CambioFisico {
  character01: string
  character02: string
  character04: string
  cantidadPendiente: number
  character06: string
}
```

Y agregar estas dos entradas al objeto `api` existente:

```typescript
  cambiosFisicos: {
    byVendor: (vendorId: string) =>
      request<CambioFisico[]>(`/api/cambios-fisicos?vendorId=${encodeURIComponent(vendorId)}`),
  },

  purchaseOrders: {
    create: (vendorId: string, comentarios: string, lineas: PurchaseOrderLine[]) =>
      request<CreatePurchaseOrderResult>('/api/purchase-orders', {
        method: 'POST',
        body: JSON.stringify({ vendorId, comentarios, lineas }),
      }),
  },
```

- [ ] **Step 2: Extender `PartsGrid.tsx`**

En `src/web/src/parts/PartsGrid.tsx`:

1. Exportar la función `isRowInvalid` (quitar el `function` sin `export` y agregarlo):

```typescript
export function isRowInvalid(row: PartRowState): boolean {
  if (!row.assign) return false
  return row.qtyToFill === null || row.qtyToFill <= 0 || row.cost <= 0
}
```

2. Agregar la prop opcional a `Props`:

```typescript
interface Props {
  vendorId: string
  onRowsChange?: (rows: PartRowState[]) => void
}
```

3. En el cuerpo de `PartsGrid`, después de la declaración de `rows`/`setRows` y de `selectedCount`, agregar:

```typescript
export function PartsGrid({ vendorId, onRowsChange }: Props) {
  // ... (todo lo existente igual) ...

  useEffect(() => {
    onRowsChange?.(rows)
  }, [rows, onRowsChange])

  // ... (resto del componente igual) ...
}
```

No cambia nada más de la lógica existente (carga, filtro, validación) — solo se agrega el aviso hacia afuera.

- [ ] **Step 3: Verificar que compila**

Run: `cd src/web && npx tsc -b --noEmit`
Expected: exit 0, sin errores

- [ ] **Step 4: Commit**

```bash
git add src/web/src/api/client.ts src/web/src/parts/PartsGrid.tsx
git commit -m "feat: add purchase order API client methods and expose PartsGrid row state"
```

---

## Task 7: Frontend — panel de comentarios, Cambios Físicos y botón Procesar

**Files:**
- Create: `src/web/src/purchaseOrders/PurchaseOrderPanel.tsx`

**Interfaces:**
- Consumes: `api.cambiosFisicos.byVendor`, `api.purchaseOrders.create`, `PartRowState`/`isRowInvalid` (de `../parts/PartsGrid`)
- Produces: `PurchaseOrderPanel` — componente con props `vendorId: string`, `rows: PartRowState[]`, `canCreateOrders: boolean`

**Nota de diseño — por qué "Comentarios" no incluye los Cambios Físicos:** la Function (Task 3) los consulta y agrega por su cuenta al `CommentText`, en el momento de crear la orden — no dependen de lo que esta pantalla llegó a mostrar (spec, sección 6). Por eso el textarea de este panel es **solo** el texto libre del comprador; los Cambios Físicos se muestran aparte, de solo lectura, como vista previa informativa.

**Nota de diseño — validación antes de procesar:** el botón se deshabilita si no hay ninguna fila marcada y válida, si hay alguna fila marcada pero inválida (cantidad o costo ≤ 0 — defecto 10.1), o si `canCreateOrders` es `false` (criterio de aceptación 10) — en ese último caso se explica por qué con un mensaje, no solo se deshabilita en silencio.

- [ ] **Step 1: Escribir el componente**

Crear `src/web/src/purchaseOrders/PurchaseOrderPanel.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { api, type CambioFisico } from '../api/client'
import { isRowInvalid, type PartRowState } from '../parts/PartsGrid'

interface Props {
  vendorId: string
  rows: PartRowState[]
  canCreateOrders: boolean
}

export function PurchaseOrderPanel({ vendorId, rows, canCreateOrders }: Props) {
  const [cambiosFisicos, setCambiosFisicos] = useState<CambioFisico[]>([])
  const [comentarios, setComentarios] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [poNum, setPoNum] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setCambiosFisicos([])
    setPoNum(null)
    setError(null)
    api.cambiosFisicos
      .byVendor(vendorId)
      .then(setCambiosFisicos)
      .catch(() => setCambiosFisicos([]))
  }, [vendorId])

  const selectedLines = rows.filter((r) => r.assign && !isRowInvalid(r))
  const hasInvalidSelection = rows.some((r) => r.assign && isRowInvalid(r))
  const canProcess =
    canCreateOrders && selectedLines.length > 0 && !hasInvalidSelection && !submitting

  async function handleProcesar() {
    setSubmitting(true)
    setError(null)
    try {
      const lineas = selectedLines.map((r) => ({
        partNum: r.partNum,
        cantidad: r.qtyToFill as number,
        costo: r.cost,
        uom: r.uom,
      }))
      const result = await api.purchaseOrders.create(vendorId, comentarios, lineas)
      setPoNum(result.poNum)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo crear la orden de compra.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <section>
      <h2>Comentarios</h2>

      {cambiosFisicos.length > 0 && (
        <p>
          Cambios Fisicos pendientes:{' '}
          {cambiosFisicos
            .map(
              (c) =>
                `${c.character01}  ${c.character02}  ${c.character04}  ${c.cantidadPendiente.toFixed(2)}  ${c.character06}`,
            )
            .join(' - ')}
        </p>
      )}

      <label htmlFor="comentarios">Comentarios adicionales</label>
      <textarea
        id="comentarios"
        value={comentarios}
        onChange={(e) => setComentarios(e.target.value)}
      />

      {!canCreateOrders && (
        <p role="alert">
          No tienes un comprador asignado en esta compania. No puedes crear ordenes de compra.
        </p>
      )}
      {hasInvalidSelection && (
        <p role="alert">
          Hay articulos marcados con cantidad o costo invalido. Corrigelos antes de procesar.
        </p>
      )}
      {error && <p role="alert">{error}</p>}
      {poNum !== null && <p>Orden de compra creada: OC {poNum}</p>}

      <button type="button" disabled={!canProcess} onClick={handleProcesar}>
        {submitting ? 'Procesando...' : 'Procesar'}
      </button>
    </section>
  )
}
```

- [ ] **Step 2: Verificar que compila**

Run: `cd src/web && npx tsc -b --noEmit`
Expected: exit 0, sin errores

- [ ] **Step 3: Commit**

```bash
git add src/web/src/purchaseOrders
git commit -m "feat: add purchase order panel with comments, Cambios Fisicos preview and Procesar button"
```

---

## Task 8: Integración final en `App.tsx`

**Files:**
- Modify: `src/web/src/App.tsx`

**Interfaces:**
- Consumes: `PartsGrid` (con `onRowsChange`), `PurchaseOrderPanel` (Task 7), `PartRowState` (de `./parts/PartsGrid`)
- Produces: flujo completo proveedor → grid → comentarios → procesar

- [ ] **Step 1: Reemplazar el cuerpo de `App.tsx`**

```tsx
import { useState } from 'react'
import { type Context, type Vendor } from './api/client'
import { LoginPage } from './auth/LoginPage'
import { ContextPicker } from './auth/ContextPicker'
import { useSession } from './auth/useSession'
import { VendorSearch } from './vendors/VendorSearch'
import { PartsGrid, type PartRowState } from './parts/PartsGrid'
import { PurchaseOrderPanel } from './purchaseOrders/PurchaseOrderPanel'

export default function App() {
  const { session, setSession, loading, signOut } = useSession()
  const [context, setContext] = useState<Context | null>(null)
  const [vendor, setVendor] = useState<Vendor | null>(null)
  const [rows, setRows] = useState<PartRowState[]>([])

  if (loading) return <p>Cargando...</p>

  if (!session) return <LoginPage onSignedIn={setSession} />

  if (!context) return <ContextPicker onContextSet={setContext} />

  function handleVendorSelected(selected: Vendor) {
    setVendor(selected)
    setRows([])
  }

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

      <VendorSearch onVendorSelected={handleVendorSelected} />

      {vendor && (
        <>
          <PartsGrid key={vendor.vendorId} vendorId={vendor.vendorId} onRowsChange={setRows} />
          <PurchaseOrderPanel
            key={vendor.vendorId}
            vendorId={vendor.vendorId}
            rows={rows}
            canCreateOrders={context.canCreateOrders}
          />
        </>
      )}
    </main>
  )
}
```

Nota: `key={vendor.vendorId}` también en `PurchaseOrderPanel` para que su estado (comentarios, resultado, error) se reinicie al cambiar de proveedor, igual que `PartsGrid` ya hacía desde el Plan 2.

- [ ] **Step 2: Verificar que compila**

Run: `cd src/web && npm run build`
Expected: build exitoso, sin errores

- [ ] **Step 3: Commit**

```bash
git add src/web/src/App.tsx
git commit -m "feat: wire purchase order creation into the main app flow"
```

---

## Verificación final del Plan 3

- [ ] `dotnet test` pasa completo
- [ ] `npm run build` en `src/web` compila sin errores
- [ ] El BAQ `OCA_CambiosFisicos` está publicado y verificado contra Epicor real (Task 2)
- [ ] La Function `OCACrearOC` está publicada, mapeada a la compañía correcta, y verificada contra Epicor real (Task 3) — incluyendo el caso de una línea inválida que detiene todo sin dejar OC parcial
- [ ] Un comprador ve los Cambios Físicos pendientes del proveedor antes de procesar
- [ ] Al procesar, se crea en Epicor una OC **aprobada** con el `BuyerID` del usuario (criterio de aceptación 5)
- [ ] Si la creación falla (línea inválida, proveedor bloqueado, etc.), no queda ninguna OC parcial y el comprador ve el mensaje real de Epicor, no uno genérico (criterio de aceptación 6)
- [ ] Los comentarios y los Cambios Físicos pendientes aparecen combinados una sola vez en la OC (defecto 10.6)
- [ ] Un usuario sin buyer propio no puede procesar y recibe un mensaje claro, sin perder acceso a ver el grid (criterio de aceptación 10)
- [ ] Cambiar de proveedor reinicia tanto el grid como el panel de comentarios/resultado

**Siguiente:** Plan 4 — PDF de la orden (QuestPDF) y encolado de correo (`sp_EnviaOC_V2`/`CFSJService`).
