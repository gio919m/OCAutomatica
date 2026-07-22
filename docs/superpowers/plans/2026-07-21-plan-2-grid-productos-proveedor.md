# OC Automática — Plan 2: El grid de productos por proveedor

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **Task 1 is a manual Epicor-configuration task, not a coding task — it cannot be dispatched to a coding subagent.**

**Goal:** Un comprador busca un proveedor, lo selecciona, y ve en un grid todos los productos que ese proveedor surte — con inventario, costo, mínimo/máximo y cantidad en tránsito — pudiendo marcar artículos y capturar cantidades a surtir con validación decimal, sin perder esas marcas al usar el buscador.

**Architecture:** Un nuevo BAQ en Epicor (`OCA_PartesPorProveedor`) expone la consulta que hoy vive embebida en SQL directo dentro de la customización C#. El backend la consume por REST v2 (mismo patrón que `PurAgentSvc`/`PlantSvc` del Plan 1: `IEpicorClient` + servicio tipado + controlador). El frontend agrega AG Grid Community para la tabla editable, y un buscador de proveedores sobre `Erp.BO.VendorSvc` (ya confirmado accesible en el Plan 1).

**Tech Stack:** .NET 8 (ya en uso), AG Grid Community + `ag-grid-react` (nuevo), React 19 + TypeScript (ya en uso, sin librerías de estado de servidor adicionales — se sigue el patrón `api.*` + `useState`/`useEffect` ya establecido en el Plan 1, no se introduce TanStack Query).

## Global Constraints

- **.NET 8** (`net8.0`). No usar preview features.
- **Epicor REST v2** únicamente. Ninguna conexión directa a SQL Server.
- **Ninguna credencial en código ni en `appsettings.json` versionado.**
- **El navegador nunca recibe credenciales de Epicor.**
- **NO usar FluentAssertions.** Solo asserts de xUnit.
- Ambiente de desarrollo: **el Epicor de pruebas** (`CFSJ_LAF`), nunca producción.
- Mensajes de la interfaz en **español**; código y comentarios en **inglés**.
- Cantidades y costos se manejan como **decimales** en todo el flujo — nunca `Convert.ToInt32`/`parseInt` (defecto 10.1 del spec original: `Convert.ToInt32` sobre una cantidad `Double` rechazaba cualquier valor menor a 0.5).
- La búsqueda/filtro del grid es **puramente visual** — nunca debe alterar qué filas están marcadas ni sus cantidades capturadas (defecto 10.2 del spec original).
- Seguir los patrones ya establecidos en el Plan 1: servicios con `IEpicorClient` inyectado, controladores que verifican `HttpContext.Items[SessionMiddleware.ItemKey]`, resuelven credenciales con `_sessions.GetCredentials(session.SessionId)`, y atrapan `EpicorException` mapeando `ex.Reason` a un mensaje en español vía un helper `HandleEpicorException`.

---

## Contexto: interfaces ya existentes del Plan 1 (no se repiten en este plan, solo se consumen)

- `IEpicorClient.GetAsync<T>(string company, string relativePath, EpicorCredentials credentials, CancellationToken ct = default)` — `src/OCAutomatica.Api/Epicor/IEpicorClient.cs`
- `EpicorException { int StatusCode; EpicorErrorReason Reason; }`, `EpicorErrorReason { InvalidCredentials, InvalidApiKey, AccessDenied, Other }` — `src/OCAutomatica.Api/Epicor/`
- `EpicorCredentials(string Username, string Password)` — con `ToString()` que nunca imprime el password
- `ISessionStore.Get(string sessionId)` → `UserSession?`, `GetCredentials(string sessionId)` → `EpicorCredentials?`
- `UserSession { string SessionId; string Username; string Company; string Plant; string? BuyerId; }`
- `SessionMiddleware.ItemKey` (const `"UserSession"`) — usado como `HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session`
- Frontend: `src/web/src/api/client.ts` exporta `api = { companies, login, logout, me, plants, setContext }` y la clase `ApiError { status: number }`. `src/web/src/App.tsx` renderiza `LoginPage` → `ContextPicker` → (placeholder actual: "El grid de productos llega en el Plan 2", que este plan reemplaza).

---

## Estructura de archivos

```
src/OCAutomatica.Api/
├── Vendors/
│   ├── VendorModels.cs        # Vendor record + DTOs de Erp.BO.VendorSvc
│   ├── IVendorService.cs
│   └── VendorService.cs
├── Parts/
│   ├── PartModels.cs           # PartRow record + DTOs del BAQ
│   ├── IPartService.cs
│   └── PartService.cs
├── Controllers/
│   ├── VendorsController.cs    # GET /api/vendors?search=
│   └── PartsController.cs      # GET /api/parts?vendorId=
└── Program.cs                  # (modificar: registrar los 2 servicios nuevos)

src/web/src/
├── api/client.ts                # (modificar: agregar tipos Vendor/PartRow + api.vendors/api.parts)
├── vendors/
│   └── VendorSearch.tsx
├── parts/
│   ├── PartsGrid.tsx
│   └── SelectionCounter.tsx
└── App.tsx                      # (modificar: reemplazar el placeholder del Plan 1)

tests/OCAutomatica.Api.Tests/
├── Vendors/VendorServiceTests.cs
└── Parts/PartServiceTests.cs
```

---

## Task 1: Publicar el BAQ `OCA_PartesPorProveedor` en Epicor

**Este es un paso manual en Epicor, no una tarea de código.** No la despaches a un subagente implementador — requiere acceso al BAQ Designer de Kinetic, que ningún subagente tiene.

**Estado: completo.** El BAQ ya fue construido, publicado y verificado por REST contra datos reales (secciones 1.4, 1.5 y 1.7 ya están actualizadas con los hallazgos reales, no con el diseño original), incluyendo los dos campos calculados `Inventario`/`CantidadEnTransito` — ya presentes en la respuesta REST real, aunque con el nombre `Calculated_Inventario`/`Calculated_CantidadEnTransito` (ver 1.5).

**Por qué existe esta tarea:** el grid necesita reemplazar el SQL directo que hoy vive en `GetDataIntoDT` (la customización C# original) por un BAQ expuesto vía REST v2 — así lo definimos en la sección 6 del spec de diseño.

### 1.1 — Tablas y joins

Crea un nuevo BAQ con ID **`OCA_PartesPorProveedor`**, tabla base **`VendPart`** (alias `VendPart`), y agrega estos joins en este orden:

| # | Tabla | Alias | Tipo de join | Condición |
|---|---|---|---|---|
| 1 | `Vendor` | `Vendor` | Inner Join | `VendPart.Company = Vendor.Company` AND `VendPart.VendorNum = Vendor.VendorNum` |
| 2 | `Part` | `Part` | Left Outer Join | `VendPart.Company = Part.Company` AND `VendPart.PartNum = Part.PartNum` |
| 3 | `PartPlant` | `PartPlant` | Left Outer Join | `VendPart.Company = PartPlant.Company` AND `VendPart.PartNum = PartPlant.PartNum` AND `VendPart.EffectiveDate >= '01/01/2021'` |
| 4 | `PartPC` | `PartPC13` | Left Outer Join | `VendPart.Company = PartPC13.Company` AND `VendPart.PartNum = PartPC13.PartNum` AND `PartPC13.PCType = 'EAN-13'` AND `Part.IUM = PartPC13.UOMCode` |
| 5 | `PartPC` | `PartPC14` | Left Outer Join | `VendPart.Company = PartPC14.Company` AND `VendPart.PartNum = PartPC14.PartNum` AND `PartPC14.PCType = 'EAN-14'` AND `Part.IUM = PartPC14.UOMCode` |

`PartPC` se agrega **dos veces** con alias distintos (`PartPC13`, `PartPC14`) — el BAQ Designer soporta agregar la misma tabla más de una vez con alias diferentes; es exactamente lo que hacía el SQL original con dos `LEFT JOIN erp.partpc`.

### 1.2 — Subconsultas (agregados)

Crea estas dos subconsultas (Sub-Query) dentro del mismo BAQ:

**Subconsulta `InventarioSQ`** — reemplaza el `CTE3` del SQL original:
- Tablas: `PartBin` (alias `PartBin`) inner join `Warehse` (alias `Warehse`) on `PartBin.Company = Warehse.Company` AND `PartBin.WarehouseCode = Warehse.WarehouseCode`
- Group By: `PartBin.PartNum`
- Campo calculado agregado: `Sum(PartBin.OnHandQty)` con alias `TotalBin`
- Criteria: `Warehse.Plant = CurrentPlant` (ver parámetro real confirmado en 1.4)

**Subconsulta `TransitoSQ`** — reemplaza el `CTE4` del SQL original:
- Tablas: `POHeader` (alias `POHeader`) inner join `PODetail` (alias `PODetail`) on `POHeader.Company = PODetail.Company` AND `POHeader.VendorNum = PODetail.VendorNum` AND `POHeader.PONum = PODetail.PONum`, inner join `PORel` (alias `PORel`) on `PODetail.Company = PORel.Company` AND `PODetail.PONum = PORel.PONum` AND `PODetail.POLine = PORel.POLine`
- Group By: `PODetail.PartNum`
- Campo calculado agregado: `Sum(PODetail.XOrderQty)` con alias `XOrden`
- Criteria: `POHeader.OpenOrder = true` AND `POHeader.Approve = true` AND `PORel.Plant = CurrentPlant`

Une ambas subconsultas al query principal como Left Outer Join contra `VendPart`:
- `VendPart.PartNum = InventarioSQ.PartNum`
- `VendPart.PartNum = TransitoSQ.PartNum`

### 1.3 — Campos calculados (Calculated Fields, en el query principal)

| Nombre | Fórmula |
|---|---|
| `Inventario` | `IsNull(InventarioSQ.TotalBin, 0)` |
| `CantidadEnTransito` | `IsNull(TransitoSQ.XOrden, 0)` |

### 1.4 — Parámetros del BAQ (Criteria / Parameters)

**YA VERIFICADO CONTRA EL AMBIENTE DE PRUEBAS REAL** — el BAQ ya fue construido y probado por REST. Resultado real, reemplaza el diseño original de esta sección:

- **`CurrentCompany` no es necesario.** Se probó la llamada con y sin él (`?CurrentCompany=CFSJ_LAF&CurrentPlant=LAF&vendorId=001008` vs `?CurrentPlant=LAF&vendorId=001008`) y ambas devuelven exactamente los mismos 47 renglones — la compañía ya queda scoped por el segmento `{Company}` de la URL. **No lo declares como parámetro del BAQ.**
- **`CurrentPlant`** (así, tal cual, con esa capitalización) sí es el parámetro real usado para filtrar planta — pese a su nombre de "System Defined", acepta override directo por query string en REST sin problema. Úsalo en el criteria del query principal (`PartPlant.Plant = CurrentPlant`) y en ambas subconsultas.
- **`vendorId`** (minúscula, como lo nombró quien construyó el BAQ) es el parámetro real para el proveedor: `Vendor.VendorID = vendorId`.

El código de la Task 3 ya usa estos dos nombres reales (`CurrentPlant`, `vendorId`) — no los que se habían planeado originalmente (`PlantParam`/`VendorIDParam`).

### 1.5 — Campos a mostrar (Display Fields)

**YA VERIFICADO:** los campos ya unidos al BAQ **no** quedaron con alias limpios — Epicor los expone con su convención por defecto `TablaAlias_Campo`, confirmada vía `$metadata` y una llamada real:

| Campo origen | Nombre real en la respuesta REST |
|---|---|
| `Vendor.Name` | `Vendor_Name` |
| `Vendor.VendorID` | `Vendor_VendorID` |
| `VendPart.PartNum` | `VendPart_PartNum` |
| `Part.PartDescription` | `Part_PartDescription` |
| `Part.PUM` | `Part_PUM` |
| `PartPlant.MinimumQty` | `PartPlant_MinimumQty` |
| `PartPlant.MaximumQty` | `PartPlant_MaximumQty` |
| `VendPart.BaseUnitPrice` | `VendPart_BaseUnitPrice` |
| `PartPC13.ProdCode` (EAN-13) | `PartPC_ProdCode` |
| `PartPC14.ProdCode` (EAN-14) | `PartPC1_ProdCode` |

No hace falta renombrarlos — el DTO de la Task 3 ya usa estos nombres reales tal cual. También aparece siempre un campo `RowIdent` (Guid) que Epicor agrega automáticamente a todo BAQ; no se usa en el DTO.

**YA AGREGADO Y VERIFICADO:** los campos calculados `Inventario` y `CantidadEnTransito` ya se agregaron a los Displayed Fields y se confirmaron por REST contra el proveedor `001008` (47 renglones: 32 con `Inventario` distinto de cero, 21 con `CantidadEnTransito` distinto de cero — ambos como `number`, nunca `null`). **A diferencia de lo esperado, Epicor sí les antepuso un prefijo**, porque no son calculated fields "libres" sino resultados de subconsultas expuestos como calculated field del query principal — Epicor los nombra con el prefijo genérico `Calculated_`, no con el nombre de ninguna tabla:

| Campo calculado | Nombre real en la respuesta REST |
|---|---|
| `Inventario` | `Calculated_Inventario` |
| `CantidadEnTransito` | `Calculated_CantidadEnTransito` |

El DTO de la Task 3 ya usa estos dos nombres reales (`Calculated_Inventario`, `Calculated_CantidadEnTransito`).

### 1.6 — Publicar y dar acceso

**YA HECHO** — el BAQ está publicado, con REST API activo, y el Access Scope del API key ya lo incluye (confirmado: la llamada real devolvió 200, no 401/403 de permisos).

### 1.7 — Verificación real (ya ejecutada por el controller)

Endpoint real (nota el sub-recurso `/Data` — `BaqSvc/{BAQID}` por sí solo solo devuelve el descriptor del recurso, no las filas):

```
GET /api/v2/odata/CFSJ_LAF/BaqSvc/OCA_PartesPorProveedor/Data?CurrentPlant=LAF&vendorId=001008
```

Resultado real (segunda verificación, ya con los campos calculados agregados): HTTP 200, 47 renglones para el proveedor `001008`, primera fila:
```json
{
  "Vendor_Name": "JARAMILLO TREVIÑO GERARDO MAGDALENO",
  "Vendor_VendorID": "001008",
  "VendPart_PartNum": "1011500784",
  "Part_PartDescription": "HOJA PARA TAMAL KG",
  "Part_PUM": "KGS",
  "PartPlant_MinimumQty": 9,
  "PartPlant_MaximumQty": 18,
  "VendPart_BaseUnitPrice": 115,
  "PartPC_ProdCode": "000084",
  "PartPC1_ProdCode": null,
  "Calculated_Inventario": 0,
  "Calculated_CantidadEnTransito": 0,
  "RowIdent": "00000001-0000-0000-0000-000000000000"
}
```

Coincide con la captura de pantalla original de la customización C# (mismo proveedor, mismo primer artículo "HOJA PARA TAMAL KG"). Esta primera fila en particular tiene `0` en ambos campos calculados, pero eso es un valor real de negocio, no una falla: al revisar las 47 filas completas, 32 tienen `Calculated_Inventario` distinto de cero (ej. `8310400932` MANGO KG con `8.37`) y 21 tienen `Calculated_CantidadEnTransito` distinto de cero (ej. el mismo `8310400932` con `5`) — confirma que las subconsultas `InventarioSQ`/`TransitoSQ` sí están calculando correctamente. **Task 1 cerrada end-to-end.**

---

## Task 2: Búsqueda de proveedores (backend)

**Files:**
- Create: `src/OCAutomatica.Api/Vendors/VendorModels.cs`
- Create: `src/OCAutomatica.Api/Vendors/IVendorService.cs`
- Create: `src/OCAutomatica.Api/Vendors/VendorService.cs`
- Create: `src/OCAutomatica.Api/Controllers/VendorsController.cs`
- Modify: `src/OCAutomatica.Api/Program.cs`
- Test: `tests/OCAutomatica.Api.Tests/Vendors/VendorServiceTests.cs`

**Interfaces:**
- Consumes: `IEpicorClient`, `EpicorCredentials`, `EpicorException`, `EpicorErrorReason`, `ISessionStore`, `UserSession`, `SessionMiddleware.ItemKey`
- Produces:
  - `record Vendor(string VendorId, string Name)`
  - `interface IVendorService { Task<IReadOnlyList<Vendor>> SearchAsync(string company, string search, EpicorCredentials credentials, CancellationToken ct = default) }`
  - `GET /api/vendors?search={texto}` → `Vendor[]`

**Nota de diseño:** `Erp.BO.VendorSvc` ya se confirmó accesible en el Plan 1 (se usa como ping de login en `AuthService`). El filtro de búsqueda se hace con OData `$filter` sobre `Name` y `VendorID`, limitado con `$top=20` para no traer el catálogo completo en cada tecleo.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/OCAutomatica.Api.Tests/Vendors/VendorServiceTests.cs`:

```csharp
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Vendors;

namespace OCAutomatica.Api.Tests.Vendors;

public class VendorServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly object? _response;
        public string? LastRelativePath { get; private set; }

        public StubEpicorClient(object? response) => _response = response;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
        {
            LastRelativePath = relativePath;
            return Task.FromResult((T?)_response);
        }

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_response);
    }

    private static readonly EpicorCredentials Creds = new("jyanez", "x");

    private static VendorListResponse Response(params (string Id, string Name)[] vendors)
        => new()
        {
            Value = vendors
                .Select(v => new VendorDto { VendorID = v.Id, Name = v.Name })
                .ToList()
        };

    [Fact]
    public async Task SearchAsync_MapsEpicorResponse()
    {
        var client = new StubEpicorClient(Response(
            ("001008", "JARAMILLO TREVINO GERARDO MAGDALENO"),
            ("002801", "ALANIS VILLARREAL JULIO CESAR")));
        var service = new VendorService(client);

        var vendors = await service.SearchAsync("CFSJ_LAF", "JARAMILLO", Creds);

        Assert.Equal(2, vendors.Count);
        Assert.Equal("001008", vendors[0].VendorId);
        Assert.Equal("JARAMILLO TREVINO GERARDO MAGDALENO", vendors[0].Name);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyWhenNoResponse()
    {
        var service = new VendorService(new StubEpicorClient(null));

        var vendors = await service.SearchAsync("CFSJ_LAF", "nada", Creds);

        Assert.Empty(vendors);
    }

    [Fact]
    public async Task SearchAsync_EscapesSearchTermInFilter()
    {
        // Prevents a vendor name containing a single quote from breaking the
        // OData $filter (same class of bug as the legacy sp_EnviaOC_V2 SQL
        // injection — but here it's an OData filter, so the fix is escaping
        // the embedded quote by doubling it, OData's own escape convention.
        var client = new StubEpicorClient(Response());
        var service = new VendorService(client);

        await service.SearchAsync("CFSJ_LAF", "O'BRIEN", Creds);

        Assert.Contains("O''BRIEN", client.LastRelativePath);
    }

    [Fact]
    public async Task SearchAsync_LimitsResultsToTwenty()
    {
        var client = new StubEpicorClient(Response());
        var service = new VendorService(client);

        await service.SearchAsync("CFSJ_LAF", "a", Creds);

        Assert.Contains("$top=20", client.LastRelativePath);
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter VendorServiceTests`
Expected: FAIL — `VendorService`, `VendorListResponse`, `VendorDto` no existen

- [ ] **Step 3: Implementar los modelos**

Crear `src/OCAutomatica.Api/Vendors/VendorModels.cs`:

```csharp
namespace OCAutomatica.Api.Vendors;

public sealed record Vendor(string VendorId, string Name);

public sealed class VendorListResponse
{
    public List<VendorDto> Value { get; set; } = new();
}

public sealed class VendorDto
{
    public string VendorID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Implementar la interfaz**

Crear `src/OCAutomatica.Api/Vendors/IVendorService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Vendors;

public interface IVendorService
{
    Task<IReadOnlyList<Vendor>> SearchAsync(
        string company,
        string search,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
```

- [ ] **Step 5: Implementar `VendorService`**

Crear `src/OCAutomatica.Api/Vendors/VendorService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Vendors;

public sealed class VendorService : IVendorService
{
    private readonly IEpicorClient _epicor;

    public VendorService(IEpicorClient epicor) => _epicor = epicor;

    public async Task<IReadOnlyList<Vendor>> SearchAsync(
        string company,
        string search,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        // OData escapes an embedded single quote by doubling it.
        var escaped = search.Replace("'", "''");
        var relativePath =
            $"Erp.BO.VendorSvc/Vendors?$filter=contains(Name,'{escaped}') or contains(VendorID,'{escaped}')&$top=20";

        var response = await _epicor.GetAsync<VendorListResponse>(
            company, relativePath, credentials, ct);

        if (response is null) return Array.Empty<Vendor>();

        return response.Value
            .Select(v => new Vendor(v.VendorID, v.Name))
            .ToList();
    }
}
```

- [ ] **Step 6: Correr los tests para verificar que pasan**

Run: `dotnet test --filter VendorServiceTests`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 7: Escribir el controlador**

Crear `src/OCAutomatica.Api/Controllers/VendorsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Vendors;

namespace OCAutomatica.Api.Controllers;

[ApiController]
[Route("api/vendors")]
public sealed class VendorsController : ControllerBase
{
    private readonly IVendorService _vendors;
    private readonly ISessionStore _sessions;
    private readonly ILogger<VendorsController> _logger;

    public VendorsController(
        IVendorService vendors,
        ISessionStore sessions,
        ILogger<VendorsController> logger)
    {
        _vendors = vendors;
        _sessions = sessions;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string search, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(search)) return Ok(Array.Empty<Vendor>());

        try
        {
            var vendors = await _vendors.SearchAsync(session.Company, search, credentials, ct);
            return Ok(vendors);
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while searching vendors for {Username} on {Company}",
                ex.Reason, session.Username, session.Company);
            return HandleEpicorException(ex);
        }
    }

    private IActionResult HandleEpicorException(EpicorException ex)
    {
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
}
```

- [ ] **Step 8: Registrar el servicio en `Program.cs`**

En `src/OCAutomatica.Api/Program.cs`, agregar el `using`:

```csharp
using OCAutomatica.Api.Vendors;
```

Y junto a los demás registros de servicios:

```csharp
builder.Services.AddScoped<IVendorService, VendorService>();
```

- [ ] **Step 9: Verificar que todo compila y pasa**

Run: `dotnet test`
Expected: `Passed! - Failed: 0, Passed: 34`

- [ ] **Step 10: Commit**

```bash
git add src/OCAutomatica.Api/Vendors src/OCAutomatica.Api/Controllers/VendorsController.cs src/OCAutomatica.Api/Program.cs tests/OCAutomatica.Api.Tests/Vendors
git commit -m "feat: add vendor search backed by Erp.BO.VendorSvc"
```

---

## Task 3: Partes por proveedor (backend)

**Precondición cumplida: la Task 1 ya tiene los campos `Inventario`/`CantidadEnTransito` agregados y verificados por REST** (sección 1.5/1.7) — llegan como `Calculated_Inventario`/`Calculated_CantidadEnTransito`. El código de abajo ya usa el endpoint (`BaqSvc/OCA_PartesPorProveedor/Data`), los parámetros (`CurrentPlant`, `vendorId`) y los nombres de campo reales confirmados por REST, incluyendo el prefijo `Calculated_` en los dos campos calculados.

**Files:**
- Create: `src/OCAutomatica.Api/Parts/PartModels.cs`
- Create: `src/OCAutomatica.Api/Parts/IPartService.cs`
- Create: `src/OCAutomatica.Api/Parts/PartService.cs`
- Create: `src/OCAutomatica.Api/Controllers/PartsController.cs`
- Modify: `src/OCAutomatica.Api/Program.cs`
- Test: `tests/OCAutomatica.Api.Tests/Parts/PartServiceTests.cs`

**Interfaces:**
- Consumes: `IEpicorClient`, `EpicorCredentials`, `EpicorException`, `EpicorErrorReason`, `ISessionStore`, `UserSession`, `SessionMiddleware.ItemKey`
- Produces:
  - `record PartRow(string VendorId, string VendorName, string PartNum, string PartDescription, string Uom, decimal MinimumQty, decimal MaximumQty, decimal Cost, string Ean13, string Ean14, decimal OnHandQty, decimal InTransitQty)`
  - `interface IPartService { Task<IReadOnlyList<PartRow>> GetByVendorAsync(string company, string plant, string vendorId, EpicorCredentials credentials, CancellationToken ct = default) }`
  - `GET /api/parts?vendorId={id}` → `PartRow[]`

**Nota de diseño:** todas las cantidades y costos son `decimal`, nunca `int`/`double` truncado — es exactamente la corrección del defecto 10.1 del spec (la validación original con `Convert.ToInt32` rechazaba 0.5 kg).

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/OCAutomatica.Api.Tests/Parts/PartServiceTests.cs`:

```csharp
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Parts;

namespace OCAutomatica.Api.Tests.Parts;

public class PartServiceTests
{
    private sealed class StubEpicorClient : IEpicorClient
    {
        private readonly object? _response;
        public string? LastRelativePath { get; private set; }

        public StubEpicorClient(object? response) => _response = response;

        public Task<T?> GetAsync<T>(string company, string relativePath,
            EpicorCredentials credentials, CancellationToken ct = default)
        {
            LastRelativePath = relativePath;
            return Task.FromResult((T?)_response);
        }

        public Task<T?> PostAsync<T>(string company, string relativePath, object body,
            EpicorCredentials credentials, CancellationToken ct = default)
            => Task.FromResult((T?)_response);
    }

    private static readonly EpicorCredentials Creds = new("jyanez", "x");

    private static PartListResponse Response(params PartDto[] parts)
        => new() { Value = parts.ToList() };

    private static PartDto Part(string partNum, decimal onHand, decimal cost) => new()
    {
        Vendor_VendorID = "001008",
        Vendor_Name = "JARAMILLO TREVINO GERARDO MAGDALENO",
        VendPart_PartNum = partNum,
        Part_PartDescription = "MANGO KG",
        Part_PUM = "KGS",
        PartPlant_MinimumQty = 5m,
        PartPlant_MaximumQty = 50m,
        VendPart_BaseUnitPrice = cost,
        PartPC_ProdCode = "056",
        PartPC1_ProdCode = "",
        Calculated_Inventario = onHand,
        Calculated_CantidadEnTransito = 2m
    };

    [Fact]
    public async Task GetByVendorAsync_MapsEpicorResponse()
    {
        var client = new StubEpicorClient(Response(Part("8310400932", 13.845m, 53m)));
        var service = new PartService(client);

        var parts = await service.GetByVendorAsync("CFSJ_LAF", "LAF", "001008", Creds);

        Assert.Single(parts);
        Assert.Equal("8310400932", parts[0].PartNum);
        Assert.Equal(13.845m, parts[0].OnHandQty);
        Assert.Equal(53m, parts[0].Cost);
    }

    [Fact]
    public async Task GetByVendorAsync_PreservesDecimalPrecision()
    {
        // Guards against the legacy Convert.ToInt32 bug: 0.5 kg must survive
        // the round trip, not be rounded to 0 or 1.
        var client = new StubEpicorClient(Response(Part("X", 0.5m, 12.345m)));
        var service = new PartService(client);

        var parts = await service.GetByVendorAsync("CFSJ_LAF", "LAF", "001008", Creds);

        Assert.Equal(0.5m, parts[0].OnHandQty);
        Assert.Equal(12.345m, parts[0].Cost);
    }

    [Fact]
    public async Task GetByVendorAsync_ReturnsEmptyWhenNoResponse()
    {
        var service = new PartService(new StubEpicorClient(null));

        var parts = await service.GetByVendorAsync("CFSJ_LAF", "LAF", "001008", Creds);

        Assert.Empty(parts);
    }

    [Fact]
    public async Task GetByVendorAsync_PassesPlantAndVendorAsBaqParameters()
    {
        var client = new StubEpicorClient(Response());
        var service = new PartService(client);

        await service.GetByVendorAsync("CFSJ_LAF", "LAF", "001008", Creds);

        // Confirmed by live verification: BaqSvc/{BAQID} alone only returns the
        // resource descriptor ({"name":"Data","kind":"EntitySet",...}) — the
        // actual rows live under the /Data sub-resource. CurrentCompany was
        // tested and found unnecessary (company is already scoped by the URL).
        Assert.Contains("BaqSvc/OCA_PartesPorProveedor/Data", client.LastRelativePath);
        Assert.Contains("CurrentPlant=LAF", client.LastRelativePath);
        Assert.Contains("vendorId=001008", client.LastRelativePath);
    }
}
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter PartServiceTests`
Expected: FAIL — `PartService`, `PartListResponse`, `PartDto` no existen

- [ ] **Step 3: Implementar los modelos**

Crear `src/OCAutomatica.Api/Parts/PartModels.cs`:

```csharp
namespace OCAutomatica.Api.Parts;

public sealed record PartRow(
    string VendorId,
    string VendorName,
    string PartNum,
    string PartDescription,
    string Uom,
    decimal MinimumQty,
    decimal MaximumQty,
    decimal Cost,
    string Ean13,
    string Ean14,
    decimal OnHandQty,
    decimal InTransitQty);

public sealed class PartListResponse
{
    public List<PartDto> Value { get; set; } = new();
}

// Field names confirmed by live verification against the test environment
// (Task 1, section 1.5/1.7). The BAQ was not given custom Display Names, so
// Epicor exposes joined fields with its default TableAlias_Field convention.
// Inventario/CantidadEnTransito are calculated fields sourced from subqueries,
// so Epicor prefixes them with the generic "Calculated_" alias (confirmed in
// the BAQ Designer's Display Fields grid: alias Calculated_Inventario, label
// "Inventario") rather than any table alias.
public sealed class PartDto
{
    public string Vendor_VendorID { get; set; } = string.Empty;
    public string Vendor_Name { get; set; } = string.Empty;
    public string VendPart_PartNum { get; set; } = string.Empty;
    public string Part_PartDescription { get; set; } = string.Empty;
    public string Part_PUM { get; set; } = string.Empty;
    public decimal PartPlant_MinimumQty { get; set; }
    public decimal PartPlant_MaximumQty { get; set; }
    public decimal VendPart_BaseUnitPrice { get; set; }
    public string? PartPC_ProdCode { get; set; }
    public string? PartPC1_ProdCode { get; set; }
    public decimal Calculated_Inventario { get; set; }
    public decimal Calculated_CantidadEnTransito { get; set; }
}
```

- [ ] **Step 4: Implementar la interfaz**

Crear `src/OCAutomatica.Api/Parts/IPartService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Parts;

public interface IPartService
{
    Task<IReadOnlyList<PartRow>> GetByVendorAsync(
        string company,
        string plant,
        string vendorId,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
```

- [ ] **Step 5: Implementar `PartService`**

Crear `src/OCAutomatica.Api/Parts/PartService.cs`:

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.Parts;

public sealed class PartService : IPartService
{
    private readonly IEpicorClient _epicor;

    public PartService(IEpicorClient epicor) => _epicor = epicor;

    public async Task<IReadOnlyList<PartRow>> GetByVendorAsync(
        string company,
        string plant,
        string vendorId,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        // BaqSvc/{BAQID} alone only returns the resource descriptor — the
        // actual rows live under the /Data sub-resource. CurrentCompany is
        // deliberately not sent: confirmed by live testing that the company
        // is already scoped by the URL's {Company} segment and passing it
        // made no difference to the result.
        var relativePath =
            $"BaqSvc/OCA_PartesPorProveedor/Data?CurrentPlant={Uri.EscapeDataString(plant)}" +
            $"&vendorId={Uri.EscapeDataString(vendorId)}";

        var response = await _epicor.GetAsync<PartListResponse>(
            company, relativePath, credentials, ct);

        if (response is null) return Array.Empty<PartRow>();

        return response.Value
            .Select(p => new PartRow(
                p.Vendor_VendorID,
                p.Vendor_Name,
                p.VendPart_PartNum,
                p.Part_PartDescription,
                p.Part_PUM,
                p.PartPlant_MinimumQty,
                p.PartPlant_MaximumQty,
                p.VendPart_BaseUnitPrice,
                p.PartPC_ProdCode ?? string.Empty,
                p.PartPC1_ProdCode ?? string.Empty,
                p.Calculated_Inventario,
                p.Calculated_CantidadEnTransito))
            .ToList();
    }
}
```

- [ ] **Step 6: Correr los tests para verificar que pasan**

Run: `dotnet test --filter PartServiceTests`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 7: Escribir el controlador**

Crear `src/OCAutomatica.Api/Controllers/PartsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using OCAutomatica.Api.Auth;
using OCAutomatica.Api.Epicor;
using OCAutomatica.Api.Parts;

namespace OCAutomatica.Api.Controllers;

[ApiController]
[Route("api/parts")]
public sealed class PartsController : ControllerBase
{
    private readonly IPartService _parts;
    private readonly ISessionStore _sessions;
    private readonly ILogger<PartsController> _logger;

    public PartsController(
        IPartService parts,
        ISessionStore sessions,
        ILogger<PartsController> logger)
    {
        _parts = parts;
        _sessions = sessions;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetByVendor([FromQuery] string vendorId, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(vendorId))
            return BadRequest(new { message = "Es necesario indicar un proveedor." });

        try
        {
            var parts = await _parts.GetByVendorAsync(
                session.Company, session.Plant, vendorId, credentials, ct);
            return Ok(parts);
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while fetching parts for vendor {VendorId} on {Company}",
                ex.Reason, vendorId, session.Company);
            return HandleEpicorException(ex);
        }
    }

    private IActionResult HandleEpicorException(EpicorException ex)
    {
        var message = ex.Reason switch
        {
            EpicorErrorReason.InvalidApiKey =>
                "La aplicacion no pudo autenticarse con Epicor (clave de API invalida). Avisa a sistemas.",
            EpicorErrorReason.AccessDenied =>
                "Tu usuario de Epicor no tiene permiso para consultar este BAQ. Pide que revisen tu perfil de seguridad en Epicor.",
            _ =>
                "No se pudo contactar a Epicor. Intenta de nuevo o avisa a sistemas."
        };

        return StatusCode(503, new { message });
    }
}
```

- [ ] **Step 8: Registrar el servicio en `Program.cs`**

En `src/OCAutomatica.Api/Program.cs`, agregar el `using`:

```csharp
using OCAutomatica.Api.Parts;
```

Y junto a los demás registros de servicios:

```csharp
builder.Services.AddScoped<IPartService, PartService>();
```

- [ ] **Step 9: Verificar que todo compila y pasa**

Run: `dotnet test`
Expected: `Passed! - Failed: 0, Passed: 38`

- [ ] **Step 10: Commit**

```bash
git add src/OCAutomatica.Api/Parts src/OCAutomatica.Api/Controllers/PartsController.cs src/OCAutomatica.Api/Program.cs tests/OCAutomatica.Api.Tests/Parts
git commit -m "feat: add parts-by-vendor backed by the OCA_PartesPorProveedor BAQ"
```

---

## Task 4: Frontend — cliente de API y dependencia de AG Grid

**Files:**
- Modify: `src/web/package.json` (vía `npm install`)
- Modify: `src/web/src/api/client.ts`

**Interfaces:**
- Consumes: nada nuevo (extiende el patrón `api.*` existente)
- Produces:
  - `interface Vendor { vendorId: string; name: string }`
  - `interface PartRow { vendorId: string; vendorName: string; partNum: string; partDescription: string; uom: string; minimumQty: number; maximumQty: number; cost: number; ean13: string; ean14: string; onHandQty: number; inTransitQty: number }`
  - `api.vendors.search(query: string): Promise<Vendor[]>`
  - `api.parts.byVendor(vendorId: string): Promise<PartRow[]>`

- [ ] **Step 1: Instalar AG Grid Community**

```bash
cd src/web
npm install ag-grid-community ag-grid-react
```

- [ ] **Step 2: Verificar que instaló sin vulnerabilidades nuevas**

Run: `npm install` (ya ejecutado arriba, revisar su salida)
Expected: `0 vulnerabilities` o ninguna de severidad alta/crítica nueva

- [ ] **Step 3: Extender `client.ts` con los tipos y funciones nuevas**

En `src/web/src/api/client.ts`, agregar (después de `export interface Context { ... }` y antes de `export class ApiError`):

```typescript
export interface Vendor {
  vendorId: string
  name: string
}

export interface PartRow {
  vendorId: string
  vendorName: string
  partNum: string
  partDescription: string
  uom: string
  minimumQty: number
  maximumQty: number
  cost: number
  ean13: string
  ean14: string
  onHandQty: number
  inTransitQty: number
}
```

Y agregar estas dos entradas al objeto `api` existente (junto a `plants`/`setContext`):

```typescript
  vendors: {
    search: (query: string) =>
      request<Vendor[]>(`/api/vendors?search=${encodeURIComponent(query)}`),
  },

  parts: {
    byVendor: (vendorId: string) =>
      request<PartRow[]>(`/api/parts?vendorId=${encodeURIComponent(vendorId)}`),
  },
```

- [ ] **Step 4: Verificar que compila**

Run: `cd src/web && npx tsc -b --noEmit`
Expected: exit 0, sin errores

- [ ] **Step 5: Commit**

```bash
git add src/web/package.json src/web/package-lock.json src/web/src/api/client.ts
git commit -m "feat: add AG Grid dependency and vendor/part API client methods"
```

---

## Task 5: Frontend — selector de proveedor

**Files:**
- Create: `src/web/src/vendors/VendorSearch.tsx`

**Interfaces:**
- Consumes: `api.vendors.search`, `Vendor` (de `../api/client`)
- Produces: `VendorSearch` — componente con prop `onVendorSelected: (vendor: Vendor) => void`

- [ ] **Step 1: Escribir el componente**

Crear `src/web/src/vendors/VendorSearch.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { api, type Vendor } from '../api/client'

interface Props {
  onVendorSelected: (vendor: Vendor) => void
}

export function VendorSearch({ onVendorSelected }: Props) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<Vendor[]>([])
  const [selected, setSelected] = useState<Vendor | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (query.trim().length < 2 || selected) {
      setResults([])
      return
    }

    const handle = setTimeout(() => {
      api
        .vendors.search(query)
        .then(setResults)
        .catch((err) => setError(err instanceof Error ? err.message : 'Error de busqueda.'))
    }, 300)

    return () => clearTimeout(handle)
  }, [query, selected])

  function pick(vendor: Vendor) {
    setSelected(vendor)
    setQuery(vendor.name)
    setResults([])
    onVendorSelected(vendor)
  }

  function handleChange(value: string) {
    setQuery(value)
    if (selected) setSelected(null)
  }

  return (
    <div>
      <label htmlFor="vendor-search">Proveedor</label>
      <input
        id="vendor-search"
        value={query}
        onChange={(e) => handleChange(e.target.value)}
        placeholder="Buscar por nombre o codigo..."
        autoComplete="off"
      />

      {error && <p role="alert">{error}</p>}

      {results.length > 0 && (
        <ul>
          {results.map((vendor) => (
            <li key={vendor.vendorId}>
              <button type="button" onClick={() => pick(vendor)}>
                {vendor.vendorId} — {vendor.name}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
```

- [ ] **Step 2: Verificar que compila**

Run: `cd src/web && npx tsc -b --noEmit`
Expected: exit 0, sin errores

- [ ] **Step 3: Commit**

```bash
git add src/web/src/vendors
git commit -m "feat: add vendor search component with debounced lookup"
```

---

## Task 6: Frontend — grid de productos con AG Grid

**Files:**
- Create: `src/web/src/parts/PartsGrid.tsx`
- Create: `src/web/src/parts/SelectionCounter.tsx`

**Interfaces:**
- Consumes: `api.parts.byVendor`, `PartRow` (de `../api/client`), `ag-grid-react`
- Produces:
  - `interface PartRowState extends PartRow { assign: boolean; qtyToFill: number | null }`
  - `PartsGrid` — componente con props `vendorId: string`, `onSelectionChange: (selectedCount: number) => void`
  - `SelectionCounter` — componente con prop `count: number`

**Nota de diseño — validación decimal (defecto 10.1):** `qtyToFill` se maneja como `number | null`, nunca como entero truncado. Una fila es inválida (se marca visualmente) cuando `assign === true` y (`qtyToFill` es `null`, `<= 0`, o `cost <= 0`).

**Nota de diseño — búsqueda no destructiva (defecto 10.2):** el cuadro de búsqueda usa `api.setQuickFilter` de AG Grid, que solo oculta filas visualmente — el array de datos (`rowData`) en el estado de React nunca se filtra ni se recorta, así que el conteo de seleccionados y sus cantidades sobreviven aunque la fila esté oculta por el filtro.

- [ ] **Step 1: Escribir `SelectionCounter`**

Crear `src/web/src/parts/SelectionCounter.tsx`:

```tsx
interface Props {
  count: number
}

export function SelectionCounter({ count }: Props) {
  return (
    <p>
      {count === 0
        ? 'Ningun articulo seleccionado.'
        : `${count} articulo(s) seleccionado(s).`}
    </p>
  )
}
```

- [ ] **Step 2: Escribir `PartsGrid`**

Crear `src/web/src/parts/PartsGrid.tsx`:

```tsx
import { useEffect, useMemo, useState } from 'react'
import { AgGridReact } from 'ag-grid-react'
import type { ColDef, ValueGetterParams, ValueSetterParams } from 'ag-grid-community'
import { AllCommunityModule, ModuleRegistry } from 'ag-grid-community'
import { api, type PartRow } from '../api/client'
import { SelectionCounter } from './SelectionCounter'

ModuleRegistry.registerModules([AllCommunityModule])

export interface PartRowState extends PartRow {
  assign: boolean
  qtyToFill: number | null
}

interface Props {
  vendorId: string
}

function isRowInvalid(row: PartRowState): boolean {
  if (!row.assign) return false
  return row.qtyToFill === null || row.qtyToFill <= 0 || row.cost <= 0
}

export function PartsGrid({ vendorId }: Props) {
  const [rows, setRows] = useState<PartRowState[]>([])
  const [quickFilter, setQuickFilter] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setLoading(true)
    setError(null)
    api
      .parts.byVendor(vendorId)
      .then((parts) =>
        setRows(parts.map((p) => ({ ...p, assign: false, qtyToFill: null }))),
      )
      .catch((err) => setError(err instanceof Error ? err.message : 'Error al cargar productos.'))
      .finally(() => setLoading(false))
  }, [vendorId])

  const selectedCount = useMemo(() => rows.filter((r) => r.assign).length, [rows])

  function updateRow(partNum: string, patch: Partial<PartRowState>) {
    setRows((prev) =>
      prev.map((row) => (row.partNum === partNum ? { ...row, ...patch } : row)),
    )
  }

  const columnDefs = useMemo<ColDef<PartRowState>[]>(
    () => [
      {
        field: 'assign',
        headerName: 'Asignar',
        editable: true,
        cellDataType: 'boolean',
        onCellValueChanged: (e) => updateRow(e.data!.partNum, { assign: e.newValue }),
      },
      { field: 'partNum', headerName: 'No. Parte', editable: false },
      { field: 'partDescription', headerName: 'Descripcion', editable: false, flex: 1 },
      { field: 'ean13', headerName: 'EAN', editable: false },
      { field: 'minimumQty', headerName: 'Minimo', editable: false },
      { field: 'maximumQty', headerName: 'Maximos', editable: false },
      { field: 'onHandQty', headerName: 'Inventario', editable: false },
      { field: 'cost', headerName: 'Costo', editable: false },
      {
        field: 'qtyToFill',
        headerName: 'Cantidad a Surtir',
        editable: (params) => params.data?.assign === true,
        valueGetter: (params: ValueGetterParams<PartRowState>) => params.data?.qtyToFill,
        valueSetter: (params: ValueSetterParams<PartRowState>) => {
          const parsed = params.newValue === '' ? null : Number(params.newValue)
          const value = parsed === null || Number.isNaN(parsed) ? null : parsed
          updateRow(params.data!.partNum, { qtyToFill: value })
          return true
        },
        cellClassRules: {
          'cell-invalid': (params) => isRowInvalid(params.data as PartRowState),
        },
      },
      { field: 'inTransitQty', headerName: 'Cantidad En Transito', editable: false },
      { field: 'uom', headerName: 'UM', editable: false },
    ],
    [],
  )

  if (loading) return <p>Cargando productos...</p>
  if (error) return <p role="alert">{error}</p>

  return (
    <section>
      <label htmlFor="quick-filter">Buscar</label>
      <input
        id="quick-filter"
        value={quickFilter}
        onChange={(e) => setQuickFilter(e.target.value)}
        placeholder="Filtrar por descripcion, parte..."
      />

      <SelectionCounter count={selectedCount} />

      <div style={{ height: 500 }}>
        <AgGridReact<PartRowState>
          rowData={rows}
          columnDefs={columnDefs}
          quickFilterText={quickFilter}
          getRowId={(params) => params.data.partNum}
        />
      </div>
    </section>
  )
}
```

- [ ] **Step 3: Agregar el estilo mínimo para celdas inválidas**

En `src/web/src/index.css`, agregar al final:

```css
.cell-invalid {
  background-color: #fde2e2;
}
```

- [ ] **Step 4: Verificar que compila**

Run: `cd src/web && npx tsc -b --noEmit`
Expected: exit 0, sin errores

- [ ] **Step 5: Commit**

```bash
git add src/web/src/parts src/web/src/index.css
git commit -m "feat: add editable AG Grid for parts with decimal quantity validation"
```

---

## Task 7: Integración final en `App.tsx`

**Files:**
- Modify: `src/web/src/App.tsx`

**Interfaces:**
- Consumes: `VendorSearch` (Task 5), `PartsGrid` (Task 6), `Vendor` (de `../api/client`)
- Produces: flujo completo compañía/planta → proveedor → grid, reemplazando el placeholder del Plan 1

- [ ] **Step 1: Reemplazar el cuerpo de `App.tsx`**

Reemplazar `src/web/src/App.tsx`:

```tsx
import { useState } from 'react'
import { type Context, type Vendor } from './api/client'
import { LoginPage } from './auth/LoginPage'
import { ContextPicker } from './auth/ContextPicker'
import { useSession } from './auth/useSession'
import { VendorSearch } from './vendors/VendorSearch'
import { PartsGrid } from './parts/PartsGrid'

export default function App() {
  const { session, setSession, loading, signOut } = useSession()
  const [context, setContext] = useState<Context | null>(null)
  const [vendor, setVendor] = useState<Vendor | null>(null)

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

      <VendorSearch onVendorSelected={setVendor} />

      {vendor && <PartsGrid key={vendor.vendorId} vendorId={vendor.vendorId} />}
    </main>
  )
}
```

Nota: `key={vendor.vendorId}` fuerza a React a remontar `PartsGrid` al cambiar de proveedor, para que el estado de filas seleccionadas no se arrastre entre proveedores distintos.

- [ ] **Step 2: Verificar que compila**

Run: `cd src/web && npm run build`
Expected: build exitoso, sin errores

- [ ] **Step 3: Commit**

```bash
git add src/web/src/App.tsx
git commit -m "feat: wire vendor search and parts grid into the main app flow"
```

---

## Verificación final del Plan 2

- [ ] `dotnet test` pasa completo (38 tests: 30 del Plan 1 + 4 de `VendorServiceTests` + 4 de `PartServiceTests`)
- [ ] `npm run build` en `src/web` compila sin errores
- [x] El BAQ `OCA_PartesPorProveedor` está publicado y verificado contra Epicor real (Task 1, sección 1.7) — 47 renglones reales confirmados para el proveedor 001008
- [x] Los campos `Inventario`/`CantidadEnTransito` fueron agregados al BAQ y re-verificados por REST (Task 1, sección 1.5) — llegan como `Calculated_Inventario`/`Calculated_CantidadEnTransito`, con valores numéricos reales (32/47 y 21/47 filas distintas de cero respectivamente)
- [ ] Un comprador puede buscar un proveedor, seleccionarlo, y ver sus productos en el grid con inventario, costo, mínimo/máximo y cantidad en tránsito
- [ ] Se puede marcar "Asignar" y capturar una cantidad decimal (0.5 se acepta, no se redondea)
- [ ] Filtrar con el buscador del grid no borra las marcas ni las cantidades de filas que quedan ocultas
- [ ] El contador de seleccionados refleja el total real, no solo las filas visibles
- [ ] Cambiar de proveedor reinicia el grid (sin arrastrar selecciones del proveedor anterior)

**Siguiente:** Plan 3 — creación de la orden de compra (Epicor Function `OCA_CrearOC`).
