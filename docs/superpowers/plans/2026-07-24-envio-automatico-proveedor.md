# OC Automática — Plan 5: Envío automático de la OC al proveedor al crearla

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Al terminar "Procesar orden de compra" con éxito, se encola automáticamente un correo al proveedor (`oc_tipo = 1`) con los correos capturados del proveedor más el del comprador logueado — visibles y editables en un grid "Correos a los que se enviará la OC" (agregar/quitar correos manuales) antes de procesar. Si el envío falla, la OC ya creada no se ve afectada.

**Architecture:** Se extiende el `IPurchaseOrderEmailService`/`PurchaseOrderEmailService` ya existente (Plan 4) con dos métodos nuevos que comparten su misma infraestructura (`IEpicorClient`, `IUserDirectoryService`, `IOrganizationService`, `IEmailQueueRepository`): uno para calcular la lista de destinatarios (usado por el grid) y otro para el envío real (usado automáticamente tras crear la OC), que vuelve a calcular esa misma lista desde cero en vez de confiar en el estado del navegador — igual que el legacy.

**Tech Stack:** .NET 8 y React 19 + TypeScript ya en uso. Sin librerías nuevas.

## Global Constraints

- **Epicor REST v2** únicamente para datos de Epicor. Ninguna conexión SQL directa a Epicor.
- **NO usar FluentAssertions ni librerías de mocking.** Solo asserts de xUnit y stubs/fakes escritos a mano.
- Código y comentarios en **inglés**; mensajes de UI en **español**.
- Toda llamada a `IEpicorClient` con un valor interpolado en la URL debe escapar ese valor con `Uri.EscapeDataString` (doblando comillas simples primero si el valor puede contenerlas).
- El envío al proveedor **nunca bloquea ni revierte** la creación de la OC — si falla, se avisa aparte, la orden queda creada igual (fidelidad exacta al legacy: `if(EnviaServiceOC(...)) { "Proceso completado" } else { "Proceso completado, se registro la OC pero el correo no pudo ser enviado!" }`).
- El tipo "COPIA" (`emailsCopy`) **no se replica** — confirmado en el código real como una lista que se declara vacía y nunca se llena (`private String[] emailsCopy = new string[]{ };`).
- Los dos `MessageBox` informativos del legacy previos al envío **no se replican** — el grid persistente ya transmite esa información.

---

## Contexto: interfaces ya existentes (no se repiten, solo se consumen)

- `IEpicorClient.GetAsync<T>(company, relativePath, credentials, ct)` — `src/OCAutomatica.Api/Epicor/`.
- `EpicorException`, `EpicorCredentials(string Username, string Password)`.
- `ISessionStore.GetCredentials(string sessionId)` → `EpicorCredentials?`.
- `UserSession { string SessionId; string Username; string Company; string Plant; IReadOnlyList<CompanyAccess> AvailableCompanies; }` — `CompanyAccess(string Company, string CompanyName)`.
- `IUserDirectoryService.GetEmailAsync(string userId, EpicorCredentials credentials, CancellationToken ct = default)` → `Task<string?>` — `src/OCAutomatica.Api/Users/` (Plan 4, Task 2).
- `IOrganizationService.GetPlantsAsync(company, credentials, ct)` → `IReadOnlyList<Plant>`, `Plant(string PlantId, string Name)` — `src/OCAutomatica.Api/Organization/`.
- `IEmailQueueRepository.InsertAsync(EmailQueueEntry entry, CancellationToken ct = default)`, `EmailQueueEntry(string Company, string CompanyName, string Plant, string PlantName, string PoNumber, string VendorId, string VendorName, string Emails, int OcTipo)` — `src/OCAutomatica.Api/PurchaseOrders/IEmailQueueRepository.cs` (Plan 4, Task 3).
- `IPurchaseOrderEmailService` (interfaz actual, en `src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderEmailService.cs`) y su implementación `PurchaseOrderEmailService` (en `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderEmailService.cs`) — ya tienen `SendCopyToUserAsync` (Plan 4, Task 7) y el DTO privado `PoVendorInfoDto { string VendorVendorID; string VendorName; }`. Este plan **agrega** métodos a estos dos archivos existentes, no los reemplaza.
- `PurchaseOrdersController` (en `src/OCAutomatica.Api/Controllers/PurchaseOrdersController.cs`) ya tiene `_emailService`, `_sessions`, `_logger` inyectados, y el helper privado `HandleEpicorException(EpicorException ex)`. Este plan agrega dos acciones nuevas al mismo controlador.
- Frontend: `src/web/src/api/client.ts` exporta `api.purchaseOrders = { create, byVendor, lines, reportUrl, sendCopy }`, `ApiError { status }`. `src/web/src/purchaseOrders/PurchaseOrderPanel.tsx` ya tiene `handleProcesar` (llama a `api.purchaseOrders.create`, guarda `poNum`), y un `useEffect` sobre `vendorId` que carga `cambiosFisicos` y resetea el estado del panel al cambiar de proveedor. `PurchaseOrderPanel` se remonta completo por `key={vendor.vendorId}` en `App.tsx`.
- Todos los servicios de este proyecto ya están registrados en `src/OCAutomatica.Api/Program.cs` (`IEpicorClient`, `IUserDirectoryService`, `IOrganizationService`, `IEmailQueueRepository`, `IPurchaseOrderEmailService`, etc.) — **este plan no necesita tocar `Program.cs`**, a diferencia del Plan 4, porque no crea ningún servicio nuevo, solo extiende uno que ya está registrado.

---

## Estructura de archivos

```
src/OCAutomatica.Api/
├── PurchaseOrders/
│   ├── IPurchaseOrderEmailService.cs   # (modificar: + EmailRecipient, + GetRecipientsAsync, + SendToVendorAsync)
│   └── PurchaseOrderEmailService.cs    # (modificar: + implementación de ambos métodos + 2 DTOs nuevos)
└── Controllers/
    └── PurchaseOrdersController.cs     # (modificar: + GetEmailRecipients, + SendToVendor)

src/web/src/
├── api/client.ts                        # (modificar: + EmailRecipient, + emailRecipients, + sendToVendor)
└── purchaseOrders/
    └── PurchaseOrderPanel.tsx           # (modificar: grid de correos + Añadir/Quitar + envio automatico tras Procesar)

tests/OCAutomatica.Api.Tests/
└── PurchaseOrders/PurchaseOrderEmailServiceTests.cs  # (modificar: + tests de GetRecipientsAsync y SendToVendorAsync)
```

No se agrega ningún archivo nuevo — todo son extensiones a archivos que el Plan 4 ya creó y dejó registrados en DI.

---

## Task 1: `GetRecipientsAsync` — calcular los destinatarios para el grid

**Files:**
- Modify: `src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderEmailService.cs`
- Modify: `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderEmailService.cs`
- Test: `tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderEmailServiceTests.cs`

**Por qué existe esta tarea:** replica `GetListEmails` del legacy (sin COPIA, que es dato muerto — spec sección 2.1): junta las filas PROVEEDOR (correos capturados en `Vendor_UD.ud_Correo1_c/2_c/3_c`) y una fila CREADOR (correo del usuario logueado, marcada inválida si no está capturado). Este método por sí solo solo sirve para **mostrar** el grid — el envío real es la Task 2.

**Interfaces:**
- Consumes: `IEpicorClient.GetAsync<T>` (nueva ruta a `Erp.BO.VendorSvc/Vendors`), `IUserDirectoryService.GetEmailAsync` (ya inyectado en el servicio).
- Produces: `EmailRecipient(string Tipo, string Email, bool Valido)`. `IPurchaseOrderEmailService.GetRecipientsAsync(string company, string vendorId, string username, EpicorCredentials credentials, CancellationToken ct = default)` → `Task<IReadOnlyList<EmailRecipient>>`. La Task 2 y la Task 3 lo consumen.

- [ ] **Step 1: Escribir los tests que fallan**

Agregar a `tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderEmailServiceTests.cs`, dentro de la clase existente `PurchaseOrderEmailServiceTests` (usa los mismos `StubEpicorClient`, `StubUserDirectoryService`, `StubOrganizationService`, `FakeEmailQueueRepository` que ya están ahí — no los vuelvas a declarar):

```csharp
    [Fact]
    public async Task GetRecipientsAsync_ReturnsProveedorRowsAndAValidCreadorRow()
    {
        var client = new StubEpicorClient(path =>
        {
            Assert.Contains("VendorSvc", path);
            Assert.Contains("VendorID eq '001008'", path);
            return new VendorEmailsListResponse
            {
                Value = new List<VendorEmailsDto>
                {
                    new()
                    {
                        ud_Correo1_c = "pedidos@productosdelcampo.com",
                        ud_Correo2_c = "nancy.garza09@hotmail.com",
                        ud_Correo3_c = "",
                    }
                }
            };
        });
        var service = new PurchaseOrderEmailService(
            client,
            new StubUserDirectoryService("giovanni.montoya@carnessanjuan.com"),
            new StubOrganizationService(new List<Plant> { new("LAF", "LA FE") }),
            new FakeEmailQueueRepository());

        var recipients = await service.GetRecipientsAsync("CFSJ_LAF", "001008", "epicor", Creds);

        Assert.Equal(3, recipients.Count);
        Assert.Contains(recipients, r => r.Tipo == "PROVEEDOR" && r.Email == "pedidos@productosdelcampo.com" && r.Valido);
        Assert.Contains(recipients, r => r.Tipo == "PROVEEDOR" && r.Email == "nancy.garza09@hotmail.com" && r.Valido);
        Assert.Contains(recipients, r => r.Tipo == "CREADOR" && r.Email == "giovanni.montoya@carnessanjuan.com" && r.Valido);
    }

    [Fact]
    public async Task GetRecipientsAsync_SkipsBlankVendorEmailFields()
    {
        var client = new StubEpicorClient(_ => new VendorEmailsListResponse
        {
            Value = new List<VendorEmailsDto>
            {
                new() { ud_Correo1_c = "", ud_Correo2_c = "", ud_Correo3_c = "" }
            }
        });
        var service = new PurchaseOrderEmailService(
            client, new StubUserDirectoryService("epicor@carnessanjuan.com"),
            new StubOrganizationService(new List<Plant>()), new FakeEmailQueueRepository());

        var recipients = await service.GetRecipientsAsync("CFSJ_LAF", "001008", "epicor", Creds);

        Assert.DoesNotContain(recipients, r => r.Tipo == "PROVEEDOR");
        Assert.Single(recipients); // just CREADOR
    }

    [Fact]
    public async Task GetRecipientsAsync_MarksCreadorInvalid_WhenUserHasNoEmailCaptured()
    {
        var client = new StubEpicorClient(_ => new VendorEmailsListResponse());
        var service = new PurchaseOrderEmailService(
            client, new StubUserDirectoryService(null),
            new StubOrganizationService(new List<Plant>()), new FakeEmailQueueRepository());

        var recipients = await service.GetRecipientsAsync("CFSJ_LAF", "001008", "epicor", Creds);

        var creador = Assert.Single(recipients, r => r.Tipo == "CREADOR");
        Assert.False(creador.Valido);
        Assert.Equal(string.Empty, creador.Email);
    }

    [Fact]
    public async Task GetRecipientsAsync_MarksCreadorInvalid_WhenUserEmailIsMalformed()
    {
        var client = new StubEpicorClient(_ => new VendorEmailsListResponse());
        var service = new PurchaseOrderEmailService(
            client, new StubUserDirectoryService("no-es-un-correo"),
            new StubOrganizationService(new List<Plant>()), new FakeEmailQueueRepository());

        var recipients = await service.GetRecipientsAsync("CFSJ_LAF", "001008", "epicor", Creds);

        var creador = Assert.Single(recipients, r => r.Tipo == "CREADOR");
        Assert.False(creador.Valido);
    }
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter PurchaseOrderEmailServiceTests`
Expected: FAIL — `GetRecipientsAsync`, `EmailRecipient`, `VendorEmailsListResponse`, `VendorEmailsDto` no existen todavía.

- [ ] **Step 3: Agregar `EmailRecipient` y el método a la interfaz**

En `src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderEmailService.cs`, agregar el record y el método nuevo (no quitar nada de lo que ya hay):

```csharp
using OCAutomatica.Api.Epicor;

namespace OCAutomatica.Api.PurchaseOrders;

/// <summary>
/// One row of the "Correos a los que se enviará la OC" grid. Tipo is
/// "PROVEEDOR" or "CREADOR" — the frontend adds "INCLUIR" rows locally for
/// manually-typed emails, which never come from this method.
/// </summary>
public sealed record EmailRecipient(string Tipo, string Email, bool Valido);

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

    /// <summary>
    /// Builds the PROVEEDOR + CREADOR rows for the recipients grid. Does
    /// not send anything — read-only, used to populate the UI.
    /// </summary>
    Task<IReadOnlyList<EmailRecipient>> GetRecipientsAsync(
        string company,
        string vendorId,
        string username,
        EpicorCredentials credentials,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Implementar el método**

En `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderEmailService.cs`, agregar el método público (junto a `SendCopyToUserAsync`) y el método privado que consulta los correos del proveedor, y los dos DTOs nuevos al final del archivo (junto a `PoVendorInfoDto`):

```csharp
    public async Task<IReadOnlyList<EmailRecipient>> GetRecipientsAsync(
        string company,
        string vendorId,
        string username,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var recipients = new List<EmailRecipient>();

        var vendorEmails = await GetVendorEmailsAsync(company, vendorId, credentials, ct);
        recipients.AddRange(vendorEmails.Select(e => new EmailRecipient("PROVEEDOR", e, true)));

        var userEmail = await _users.GetEmailAsync(username, credentials, ct);
        recipients.Add(!string.IsNullOrWhiteSpace(userEmail) && EmailPattern.IsMatch(userEmail)
            ? new EmailRecipient("CREADOR", userEmail, true)
            : new EmailRecipient("CREADOR", string.Empty, false));

        return recipients;
    }

    private async Task<List<string>> GetVendorEmailsAsync(
        string company, string vendorId, EpicorCredentials credentials, CancellationToken ct)
    {
        var escaped = Uri.EscapeDataString(vendorId.Replace("'", "''"));
        var path = $"Erp.BO.VendorSvc/Vendors?$filter=VendorID eq '{escaped}'" +
            "&$select=ud_Correo1_c,ud_Correo2_c,ud_Correo3_c&$top=1";
        var response = await _epicor.GetAsync<VendorEmailsListResponse>(company, path, credentials, ct);
        var dto = response?.Value.FirstOrDefault();
        if (dto is null) return new List<string>();

        return new[] { dto.ud_Correo1_c, dto.ud_Correo2_c, dto.ud_Correo3_c }
            .Where(e => !string.IsNullOrWhiteSpace(e) && EmailPattern.IsMatch(e))
            .Distinct()
            .ToList();
    }
```

Al final del archivo, junto a `PoVendorInfoDto`:

```csharp
public sealed class VendorEmailsListResponse
{
    public List<VendorEmailsDto> Value { get; set; } = new();
}

public sealed class VendorEmailsDto
{
    // Raw SQL column names on Vendor_UD, confirmed exposed directly on the
    // Vendor entity by Epicor's OData layer (same pattern as other "_c"
    // fields already confirmed on the PO header in Plan 4) — verify live
    // against Swagger before Task 2's live-verification step.
    public string ud_Correo1_c { get; set; } = string.Empty;
    public string ud_Correo2_c { get; set; } = string.Empty;
    public string ud_Correo3_c { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Correr los tests para verificar que pasan**

Run: `dotnet test --filter PurchaseOrderEmailServiceTests`
Expected: `Passed! - Failed: 0, Passed: 8` (4 existentes de `SendCopyToUserAsync` + 4 nuevos).

- [ ] **Step 6: Correr toda la suite**

Run: `dotnet test`
Expected: todos los tests existentes siguen pasando (89 antes de esta tarea + 4 nuevos = 93).

- [ ] **Step 7: Commit**

```bash
git add src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderEmailService.cs src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderEmailService.cs tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderEmailServiceTests.cs
git commit -m "feat: add GetRecipientsAsync to build the PO email recipients grid"
```

---

## Task 2: `SendToVendorAsync` — el envío real (`oc_tipo=1`)

**Files:**
- Modify: `src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderEmailService.cs`
- Modify: `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderEmailService.cs`
- Test: `tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderEmailServiceTests.cs`

**Por qué existe esta tarea:** replica el envío real de `btnProc_Click` (spec sección 2.4): vuelve a calcular PROVEEDOR+CREADOR desde cero (nunca confía en lo que el navegador tenga), le agrega los correos manuales válidos, junta todo con `;`, e inserta en `interfaz_envia_oc` con `oc_tipo=1` — usando el mismo `IEmailQueueRepository` que ya existe. Nunca lanza excepción por un correo manual mal formado (se descarta en silencio, igual que el legacy solo lo rechaza al momento de "Añadir", no al momento de enviar) ni por una lista de correos vacía (el legacy manda el `EXECUTE` igual, con la lista vacía).

**Interfaces:**
- Consumes: `GetRecipientsAsync` (Task 1, mismo servicio), `IOrganizationService.GetPlantsAsync`, `IEmailQueueRepository.InsertAsync`.
- Produces: `IPurchaseOrderEmailService.SendToVendorAsync(string company, string companyName, string plant, string vendorId, string username, int poNum, IReadOnlyList<string> manualEmails, EpicorCredentials credentials, CancellationToken ct = default)` → `Task`. La Task 3 lo consume.

- [ ] **Step 1: Escribir los tests que fallan**

Agregar a `tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderEmailServiceTests.cs`:

```csharp
    private static StubEpicorClient BuildEmailServiceClient(VendorEmailsListResponse vendorEmails) => new(path =>
    {
        if (path.Contains("VendorSvc")) return vendorEmails;
        if (path.Contains("POes"))
            return new PoVendorInfoDto { VendorVendorID = "001008", VendorName = "JARAMILLO TREVIÑO GERARDO MAGDALENO" };
        throw new InvalidOperationException($"Unexpected path: {path}");
    });

    [Fact]
    public async Task SendToVendorAsync_InsertsAQueueEntry_WithOcTipo1AndAllValidEmailsJoined()
    {
        var vendorEmails = new VendorEmailsListResponse
        {
            Value = new List<VendorEmailsDto>
            {
                new() { ud_Correo1_c = "pedidos@productosdelcampo.com", ud_Correo2_c = "", ud_Correo3_c = "" }
            }
        };
        var repository = new FakeEmailQueueRepository();
        var service = new PurchaseOrderEmailService(
            BuildEmailServiceClient(vendorEmails),
            new StubUserDirectoryService("giovanni.montoya@carnessanjuan.com"),
            new StubOrganizationService(new List<Plant> { new("LAF", "LA FE") }),
            repository);

        await service.SendToVendorAsync(
            "CFSJ_LAF", "CARNES FINAS SAN JUAN LA FE", "LAF", "001008", "epicor", 3431,
            new List<string> { "nancy.garza09@hotmail.com" }, Creds);

        Assert.NotNull(repository.LastEntry);
        Assert.Equal(1, repository.LastEntry!.OcTipo);
        Assert.Equal("001008", repository.LastEntry.VendorId);
        Assert.Equal("JARAMILLO TREVIÑO GERARDO MAGDALENO", repository.LastEntry.VendorName);
        var emails = repository.LastEntry.Emails.Split(';');
        Assert.Contains("pedidos@productosdelcampo.com", emails);
        Assert.Contains("giovanni.montoya@carnessanjuan.com", emails);
        Assert.Contains("nancy.garza09@hotmail.com", emails);
        Assert.Equal(3, emails.Length);
    }

    [Fact]
    public async Task SendToVendorAsync_SilentlyDropsAMalformedManualEmail()
    {
        var repository = new FakeEmailQueueRepository();
        var service = new PurchaseOrderEmailService(
            BuildEmailServiceClient(new VendorEmailsListResponse()),
            new StubUserDirectoryService("giovanni.montoya@carnessanjuan.com"),
            new StubOrganizationService(new List<Plant> { new("LAF", "LA FE") }),
            repository);

        await service.SendToVendorAsync(
            "CFSJ_LAF", "CARNES FINAS SAN JUAN LA FE", "LAF", "001008", "epicor", 3431,
            new List<string> { "no-es-un-correo" }, Creds);

        Assert.NotNull(repository.LastEntry);
        Assert.DoesNotContain("no-es-un-correo", repository.LastEntry!.Emails);
    }

    [Fact]
    public async Task SendToVendorAsync_StillInsertsAQueueEntry_WhenVendorHasNoEmailsAndCreadorIsInvalid()
    {
        // Mirrors the legacy exactly: a missing vendor email or an invalid
        // creador email is only an informational warning, never a block.
        var repository = new FakeEmailQueueRepository();
        var service = new PurchaseOrderEmailService(
            BuildEmailServiceClient(new VendorEmailsListResponse()),
            new StubUserDirectoryService(null),
            new StubOrganizationService(new List<Plant> { new("LAF", "LA FE") }),
            repository);

        await service.SendToVendorAsync(
            "CFSJ_LAF", "CARNES FINAS SAN JUAN LA FE", "LAF", "001008", "epicor", 3431,
            new List<string>(), Creds);

        Assert.NotNull(repository.LastEntry);
        Assert.Equal(string.Empty, repository.LastEntry!.Emails);
        Assert.Equal(1, repository.LastEntry.OcTipo);
    }

    [Fact]
    public async Task SendToVendorAsync_DeduplicatesEmails_CaseInsensitively()
    {
        var vendorEmails = new VendorEmailsListResponse
        {
            Value = new List<VendorEmailsDto>
            {
                new() { ud_Correo1_c = "Nancy.Garza09@hotmail.com", ud_Correo2_c = "", ud_Correo3_c = "" }
            }
        };
        var repository = new FakeEmailQueueRepository();
        var service = new PurchaseOrderEmailService(
            BuildEmailServiceClient(vendorEmails),
            new StubUserDirectoryService(null),
            new StubOrganizationService(new List<Plant> { new("LAF", "LA FE") }),
            repository);

        await service.SendToVendorAsync(
            "CFSJ_LAF", "CARNES FINAS SAN JUAN LA FE", "LAF", "001008", "epicor", 3431,
            new List<string> { "nancy.garza09@hotmail.com" }, Creds);

        var emails = repository.LastEntry!.Emails.Split(';');
        Assert.Single(emails);
    }
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `dotnet test --filter PurchaseOrderEmailServiceTests`
Expected: FAIL — `SendToVendorAsync` no existe todavía.

- [ ] **Step 3: Agregar el método a la interfaz**

En `src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderEmailService.cs`, agregar dentro de la interfaz (junto a `GetRecipientsAsync`):

```csharp
    /// <summary>
    /// Recomputes the recipients from scratch (never trusts what the
    /// browser last fetched), merges in any manually-added emails, and
    /// queues the send with oc_tipo=1. Never throws for an empty or
    /// all-invalid recipient list — mirrors the legacy, which always
    /// queues the send regardless (a missing vendor email is only an
    /// informational warning there, never a block).
    /// </summary>
    Task SendToVendorAsync(
        string company,
        string companyName,
        string plant,
        string vendorId,
        string username,
        int poNum,
        IReadOnlyList<string> manualEmails,
        EpicorCredentials credentials,
        CancellationToken ct = default);
```

- [ ] **Step 4: Implementar el método**

En `src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderEmailService.cs`, agregar junto a `GetRecipientsAsync`:

```csharp
    public async Task SendToVendorAsync(
        string company,
        string companyName,
        string plant,
        string vendorId,
        string username,
        int poNum,
        IReadOnlyList<string> manualEmails,
        EpicorCredentials credentials,
        CancellationToken ct = default)
    {
        var recipients = await GetRecipientsAsync(company, vendorId, username, credentials, ct);

        var validManual = manualEmails
            .Where(e => !string.IsNullOrWhiteSpace(e) && EmailPattern.IsMatch(e))
            .Select(e => e.Trim());

        var allEmails = recipients
            .Where(r => r.Valido)
            .Select(r => r.Email)
            .Concat(validManual)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var escapedCompany = Uri.EscapeDataString(company.Replace("'", "''"));
        var vendorPath = $"Erp.BO.POSvc/POes('{escapedCompany}',{poNum})?$select=VendorVendorID,VendorName";
        var vendor = await _epicor.GetAsync<PoVendorInfoDto>(company, vendorPath, credentials, ct);

        var plants = await _organization.GetPlantsAsync(company, credentials, ct);
        var plantName = plants.FirstOrDefault(p => p.PlantId == plant)?.Name ?? plant;

        var entry = new EmailQueueEntry(
            company,
            companyName,
            plant,
            plantName,
            poNum.ToString(),
            vendor?.VendorVendorID ?? vendorId,
            vendor?.VendorName ?? string.Empty,
            string.Join(";", allEmails),
            1); // oc_tipo=1: automatic send to vendor + creator on OC creation (spec section 2.4)

        await _queue.InsertAsync(entry, ct);
    }
```

- [ ] **Step 5: Correr los tests para verificar que pasan**

Run: `dotnet test --filter PurchaseOrderEmailServiceTests`
Expected: `Passed! - Failed: 0, Passed: 12` (8 de la Task 1 + 4 nuevos).

- [ ] **Step 6: Correr toda la suite**

Run: `dotnet test`
Expected: `Passed! - Failed: 0, Passed: 97` (93 de la Task 1 + 4 nuevos).

- [ ] **Step 7: Verificar en vivo contra Epicor real**

Antes de seguir a la Task 3, confirmar por Swagger que `Erp.BO.VendorSvc/Vendors?$filter=VendorID eq '001008'&$select=ud_Correo1_c,ud_Correo2_c,ud_Correo3_c` devuelve los mismos correos que ya viste en el grid legacy para ese proveedor. Si el nombre de campo real es distinto, ajustar `VendorEmailsDto`.

- [ ] **Step 8: Commit**

```bash
git add src/OCAutomatica.Api/PurchaseOrders/IPurchaseOrderEmailService.cs src/OCAutomatica.Api/PurchaseOrders/PurchaseOrderEmailService.cs tests/OCAutomatica.Api.Tests/PurchaseOrders/PurchaseOrderEmailServiceTests.cs
git commit -m "feat: add SendToVendorAsync to queue the automatic oc_tipo=1 email"
```

---

## Task 3: Endpoints `GET .../email-recipients` y `POST .../send-to-vendor`

**Files:**
- Modify: `src/OCAutomatica.Api/Controllers/PurchaseOrdersController.cs`

**Por qué existe esta tarea:** conecta las Tasks 1 y 2 a URLs que el frontend puede llamar — una para llenar el grid, otra para el envío automático tras crear la OC.

**Interfaces:**
- Consumes: `IPurchaseOrderEmailService.GetRecipientsAsync` (Task 1), `IPurchaseOrderEmailService.SendToVendorAsync` (Task 2), `ISessionStore.GetCredentials`, el `HandleEpicorException` ya existente en este controlador.

- [ ] **Step 1: Agregar los dos endpoints**

En `src/OCAutomatica.Api/Controllers/PurchaseOrdersController.cs`, agregar junto a `GetByVendor`:

```csharp
    [HttpGet("email-recipients")]
    public async Task<IActionResult> GetEmailRecipients(
        [FromQuery] string vendorId, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(vendorId))
            return BadRequest(new { message = "Es necesario indicar un proveedor." });

        try
        {
            var recipients = await _emailService.GetRecipientsAsync(
                session.Company, vendorId, session.Username, credentials, ct);
            return Ok(recipients);
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while fetching email recipients for vendor {VendorId} on {Company}",
                ex.Reason, vendorId, session.Company);
            return HandleEpicorException(ex);
        }
    }
```

Agregar junto a `SendCopy`:

```csharp
    public sealed record SendToVendorApiRequest(string VendorId, List<string>? ManualEmails);

    [HttpPost("{poNum:int}/send-to-vendor")]
    public async Task<IActionResult> SendToVendor(
        int poNum, [FromBody] SendToVendorApiRequest request, CancellationToken ct)
    {
        if (HttpContext.Items[SessionMiddleware.ItemKey] is not UserSession session)
            return Unauthorized();

        var credentials = _sessions.GetCredentials(session.SessionId);
        if (credentials is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.VendorId))
            return BadRequest(new { message = "Es necesario indicar un proveedor." });

        var companyName = session.AvailableCompanies
            .FirstOrDefault(c => c.Company == session.Company)?.CompanyName ?? session.Company;

        try
        {
            await _emailService.SendToVendorAsync(
                session.Company, companyName, session.Plant, request.VendorId, session.Username, poNum,
                request.ManualEmails ?? new List<string>(), credentials, ct);
            return Ok();
        }
        catch (EpicorException ex)
        {
            _logger.LogError(ex,
                "Epicor error ({Reason}) while sending the vendor email for PO {PoNum} on {Company}",
                ex.Reason, poNum, session.Company);
            return HandleEpicorException(ex);
        }
    }
```

- [ ] **Step 2: Correr toda la suite**

Run: `dotnet test`
Expected: todos los tests existentes siguen pasando (este controlador no tiene tests automatizados propios — misma convención que el resto de `PurchaseOrdersController` desde el Plan 3).

- [ ] **Step 3: Commit**

```bash
git add src/OCAutomatica.Api/Controllers/PurchaseOrdersController.cs
git commit -m "feat: add email-recipients and send-to-vendor endpoints"
```

---

## Task 4: Frontend — grid de correos, Añadir/Quitar, y envío automático

**Files:**
- Modify: `src/web/src/api/client.ts`
- Modify: `src/web/src/purchaseOrders/PurchaseOrderPanel.tsx`

**Por qué existe esta tarea:** agrega el grid "Correos a los que se enviará la OC" a la pantalla de crear orden, con Añadir/Quitar para correos manuales, y hace que "Procesar orden de compra" dispare automáticamente el envío al proveedor justo después de crear la OC — sin botón separado, igual que el legacy.

**Interfaces:**
- Consumes: `api.purchaseOrders` (ya existente en `client.ts`).
- Produces: `api.purchaseOrders.emailRecipients(vendorId)` → `Promise<EmailRecipient[]>`, `api.purchaseOrders.sendToVendor(poNum, vendorId, manualEmails)` → `Promise<void>`.

- [ ] **Step 1: Agregar el tipo y los dos métodos a `client.ts`**

En `src/web/src/api/client.ts`, agregar la interfaz junto a `CambioFisico`:

```typescript
export interface EmailRecipient {
  tipo: string
  email: string
  valido: boolean
}
```

Y dentro de `api.purchaseOrders`, agregar junto a `sendCopy`:

```typescript
    emailRecipients: (vendorId: string) =>
      request<EmailRecipient[]>(
        `/api/purchase-orders/email-recipients?vendorId=${encodeURIComponent(vendorId)}`,
      ),

    sendToVendor: (poNum: number, vendorId: string, manualEmails: string[]) =>
      request<void>(`/api/purchase-orders/${poNum}/send-to-vendor`, {
        method: 'POST',
        body: JSON.stringify({ vendorId, manualEmails }),
      }),
```

- [ ] **Step 2: Agregar el estado y los manejadores a `PurchaseOrderPanel.tsx`**

En `src/web/src/purchaseOrders/PurchaseOrderPanel.tsx`, agregar el import de `EmailRecipient` junto al de `CambioFisico`:

```typescript
import { api, type CambioFisico, type EmailRecipient } from '../api/client'
```

Agregar el estado nuevo, junto a `comentariosDraft`:

```typescript
  const [recipients, setRecipients] = useState<EmailRecipient[]>([])
  const [manualEmails, setManualEmails] = useState<string[]>([])
  const [newEmailInput, setNewEmailInput] = useState('')
  const [emailInputError, setEmailInputError] = useState<string | null>(null)
```

En el `useEffect` que ya corre sobre `vendorId` (el que carga `cambiosFisicos` y resetea el panel), agregar el reseteo y la carga de destinatarios:

```typescript
  useEffect(() => {
    latestVendorIdRef.current = vendorId
    setCambiosFisicos([])
    setComentarios('')
    setPoNum(null)
    setError(null)
    setManualEmails([])
    setRecipients([])
    api.cambiosFisicos
      .byVendor(vendorId)
      .then((data) => {
        if (latestVendorIdRef.current === vendorId) setCambiosFisicos(data)
      })
      .catch(() => {
        if (latestVendorIdRef.current === vendorId) setCambiosFisicos([])
      })
    api.purchaseOrders
      .emailRecipients(vendorId)
      .then((data) => {
        if (latestVendorIdRef.current === vendorId) setRecipients(data)
      })
      .catch(() => {
        if (latestVendorIdRef.current === vendorId) setRecipients([])
      })
  }, [vendorId])
```

Agregar las constantes y funciones de Añadir/Quitar, cerca de `handleProcesar`:

```typescript
  const emailPattern = /^[^@\s]+@[^@\s]+\.[^@\s]+$/

  function handleAddEmail() {
    const email = newEmailInput.trim()
    if (!emailPattern.test(email)) {
      setEmailInputError(
        `La direccion de email [${email}] no tiene un formato de correo valido, revise la captura e intente de nuevo.`,
      )
      return
    }
    if (manualEmails.some((e) => e.trim().toLowerCase() === email.toLowerCase())) {
      setEmailInputError(`La direccion de email [${email}] ya existe en la lista.`)
      return
    }
    setManualEmails((prev) => [...prev, email])
    setNewEmailInput('')
    setEmailInputError(null)
  }

  function handleRemoveEmail(email: string) {
    if (!window.confirm(`¿Desea quitar la direccion de email [${email}] de la lista?`)) return
    setManualEmails((prev) => prev.filter((e) => e !== email))
  }
```

Modificar `handleProcesar` para encolar el envío al proveedor justo después de crear la OC (dentro del mismo `try`, después de `setPoNum`):

```typescript
  async function handleProcesar() {
    const submittedVendorId = vendorId
    setSubmitting(true)
    setError(null)
    setPoNum(null)
    try {
      const lineas = selectedLines.map((r) => ({
        partNum: r.partNum,
        cantidad: r.qtyToFill as number,
        costo: r.cost,
        uom: r.uom,
      }))
      const result = await api.purchaseOrders.create(vendorId, comentarios, lineas)
      if (latestVendorIdRef.current === submittedVendorId) setPoNum(result.poNum)

      try {
        await api.purchaseOrders.sendToVendor(result.poNum, vendorId, manualEmails)
      } catch {
        if (latestVendorIdRef.current === submittedVendorId) {
          setError('La orden se creo, pero el correo al proveedor no se pudo enviar.')
        }
      }
    } catch (err) {
      if (latestVendorIdRef.current === submittedVendorId) {
        setError(err instanceof Error ? err.message : 'No se pudo crear la orden de compra.')
      }
    } finally {
      setSubmitting(false)
    }
  }
```

(`setError` en el bloque de `sendToVendor` no pisa el `poNum` ya guardado — ambos mensajes, éxito de la OC y aviso del correo, pueden mostrarse a la vez, igual que el legacy mostraba dos `MessageBox` separados.)

- [ ] **Step 3: Agregar el grid y los controles al JSX**

En `src/web/src/purchaseOrders/PurchaseOrderPanel.tsx`, agregar antes del bloque `{!canCreateOrders && (...)}`:

```tsx
        <div className="field">
          <label className="field-label">Correos a los que se enviara la OC</label>
          <div className="grid-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Tipo</th>
                  <th>Email</th>
                  <th aria-label="Acciones" />
                </tr>
              </thead>
              <tbody>
                {recipients.map((r) => (
                  <tr key={`${r.tipo}-${r.email || 'sin-correo'}`}>
                    <td>{r.tipo}</td>
                    <td style={r.valido ? undefined : { color: 'var(--color-danger)' }}>
                      {r.valido ? r.email : 'Sin correo capturado'}
                    </td>
                    <td />
                  </tr>
                ))}
                {manualEmails.map((email) => (
                  <tr key={email}>
                    <td>INCLUIR</td>
                    <td>{email}</td>
                    <td>
                      <button type="button" className="btn-link" onClick={() => handleRemoveEmail(email)}>
                        Quitar
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="field-row" style={{ marginTop: 8 }}>
            <input
              type="text"
              value={newEmailInput}
              onChange={(e) => setNewEmailInput(e.target.value)}
              placeholder="Incluir en correo a..."
            />
            <button type="button" className="btn-secondary" onClick={handleAddEmail}>
              Añadir
            </button>
          </div>
          {emailInputError && <p role="alert">{emailInputError}</p>}
        </div>
```

- [ ] **Step 4: Verificar que compila**

Run: `npm run build` (desde `src/web`)
Expected: compila sin errores de TypeScript.

- [ ] **Step 5: Verificar en el navegador**

Con el backend corriendo y el frontend en modo dev:

1. Seleccionar un proveedor con correos capturados — el grid debe mostrar sus filas PROVEEDOR más una fila CREADOR con tu correo.
2. Escribir un correo en "Incluir en correo a:" y darle "Añadir" — debe aparecer como fila INCLUIR con su botón "Quitar". Intentar agregarlo de nuevo debe rechazarlo por duplicado.
3. Darle "Quitar" a esa fila — debe pedir confirmación y luego desaparecer.
4. Marcar artículos y darle "Procesar orden de compra" — al terminar, confirmar en SSMS (`192.168.100.18`) que la fila nueva en `interfaz_envia_oc` tiene `oc_tipo=1` y los correos correctos (proveedor + tu correo + cualquier manual que hayas dejado agregado).

- [ ] **Step 6: Commit**

```bash
git add src/web/src/api/client.ts src/web/src/purchaseOrders/PurchaseOrderPanel.tsx
git commit -m "feat: add the email recipients grid and automatic vendor email on Procesar"
```

---

## Verificación final

- [ ] `dotnet test` desde la raíz del repo — todo pasa (97 tests esperados).
- [ ] `npm run build` en `src/web` — compila sin errores.
- [ ] Prueba manual end-to-end (Task 4, Step 5) completa, con al menos un proveedor que sí tenga correos capturados y otro que no.
