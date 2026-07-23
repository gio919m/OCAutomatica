# Plan 4 — Reporte PDF de la OC y envío de copia por correo

## 1. Alcance

Completa las dos acciones que quedaron como placeholders deshabilitados en la
pantalla "OCs por proveedor" (ver
[2026-07-20-oc-automatica-web-design.md](2026-07-20-oc-automatica-web-design.md),
secciones 7-9):

- **Visualizar OC** — genera el PDF equivalente al reporte de Crystal actual y
  lo abre en el navegador.
- **Enviar OC por email al usuario** — manda una copia de la OC al correo del
  comprador que tiene la sesión abierta.

**Explícitamente fuera de alcance** (queda para un plan futuro):

- El envío automático al proveedor (`oc_tipo = 1`, con los correos capturados
  en `Vendor_UD`) que en el sistema actual ocurre justo después de crear la
  OC. Eso vive en el flujo de "Procesar orden de compra"
  ([Plan 3](2026-07-22-plan-3-creacion-de-oc.md)), una pantalla distinta a la
  que toca este plan, y abrirlo de nuevo no es necesario para las dos
  acciones pedidas aquí.
- Una UI para agregar correos manualmente antes de enviar.
- Conectar la columna "OC Enviada" del grid de historial a un estado real
  (ver nota al final de la sección 3).

## 2. Contexto reunido en vivo

Todo lo que sigue se confirmó con el usuario contra el sistema real (Epicor,
SSMS, el código legacy en C#) durante esta sesión de diseño — no son
suposiciones de la migración original.

### 2.1 El query real detrás del `.rpt` de Crystal

```sql
declare @dmr varchar(50)
declare @company varchar(8)
set @dmr={?dmr}
set @company='{?company}'

SELECT POH.openorder, rh.ReqNum, POH.ponum, PU.NAME, R.DUEDATE, POD.COMPANY,
       POH.entryperson, V.VENDORID, V.ADDRESS1, V.ADDRESS2, V.CITY, V.NAME,
       T.DESCRIPTION, S.DESCRIPTION, POH.orderdate, POH.termsCode,
       POH.Shipname, POD.baserevisionNum, POH.buyerID, POH.vendornum,
       POH.PrintAs, POH.currencycode, POH.exchangerate, POH.approve,
       POH.ApprovalStatus, POD.openline, pod.VoidLine, pod.ponum, pod.poline,
       pod.linedesc, pod.ium, pod.unitcost, pod.DocUnitCost, pod.OrderQty,
       pod.XOrderQty, pod.PUM, pod.costpercode, pod.partnum, pod.classid,
       pod.vendornum, pod.baseqty, pod.baseuom, pod.basepartnum, P.PRODCODE,
       CAST(POH.CommentText as varchar(2000)) CommentText,
       co.name Dato10,
       (CASE WHEN poh.ShipAddress1='' THEN co.Address1 ELSE poh.ShipAddress1 END + '  ' +
        CASE WHEN poh.ShipAddress2='' THEN co.Address2 ELSE poh.ShipAddress2 END + '  ' +
        CASE WHEN poh.ShipCity='' THEN co.city ELSE poh.ShipCity END + ' ' +
        CASE WHEN poh.ShipState='' THEN co.state ELSE poh.ShipState END + ' ' +
        CASE WHEN poh.ShipZIP='' THEN 'CP:'+co.Zip ELSE 'CP:'+poh.ShipZIP END + ' ' +
        'TEL:' + (SELECT PhoneNum FROM erp.Plant WHERE company=@company AND Plant=r.plant) + ' ' +
        'RFC: ' + co.StateTaxID) direccion,
       CO.COUNTRY, POH.DocTotalTax, r.plant, poh.TotalWhTax,
       us.[Name] as Requisitor
FROM erp.poheader AS POH
INNER JOIN ERP.PODetail AS POD ON POH.COMPANY=POD.COMPANY AND POH.PONUM=POD.PONUM
INNER JOIN ERP.VENDOR AS V ON V.COMPANY=POH.COMPANY AND V.VENDORNUM=POH.VENDORNUM
LEFT JOIN ERP.SHIPVIA AS S ON S.COMPANY=POH.COMPANY AND S.SHIPVIACODE=POH.ShipViaCode
LEFT JOIN ERP.TERMS AS T ON T.COMPANY=POH.Company AND T.TermsCode=POH.TermsCode
LEFT JOIN ERP.PARTPC AS P ON POD.COMPANY=P.COMPANY AND POD.PARTNUM=P.PARTNUM
                          AND POD.IUM=P.UOMCODE AND P.PCTYPE='EAN-13'
INNER JOIN ERP.POREL AS R ON R.COMPANY=POD.COMPANY AND R.PONUM=POD.PONUM AND R.POLine=POD.POLine
LEFT JOIN ERP.PURAGENT AS PU ON PU.COMPANY=POH.COMPANY AND PU.BUYERID=POH.BUYERID
LEFT JOIN erp.Company AS co ON co.Company=poh.Company
LEFT JOIN ERP.ReqHead AS RH ON RH.Company=R.Company AND r.ReqNum=rh.ReqNum
LEFT JOIN ERP.UserFile US ON US.DcdUserID=RH.RequestorID
WHERE POH.PONUM=@dmr AND poh.company=@company
```

Puntos que esto confirma sobre el PDF de ejemplo (`ejmploOC.pdf`, OC 3431):

- **`EAN:`** es el `P.PRODCODE` real (join a `PARTPC` con `PCType='EAN-13'`) —
  para verdura son códigos cortos, no un bug.
- **`Entrega: LOCAL`** es `S.DESCRIPTION` (`ShipVia`).
- **`Autorizado por`** es `PU.NAME` (`PurAgent`, vía `BuyerID`).
- **`Solicitado Por` / `N° Requisicion`** vienen de `ReqHead`/`UserFile` — en
  blanco salvo que la OC tenga una requisición ligada (no aplica hoy a las
  OCs creadas por esta app).
- **Domicilio de Entrega** usa `poh.ShipAddress*` si está capturado, si no
  cae al domicilio de `Company`, con teléfono de `Plant` y RFC =
  `Company.StateTaxID`.
- **`SUCURSAL:`** no aparece en ningún campo de este query — es una fórmula
  fija del `.rpt` (ver 2.2).

### 2.2 "SUCURSAL:" — reemplazo dinámico

Fórmula actual del `.rpt`:

```
IF ({Comando.plant} = "STC") THEN "SANTA CATARINA"
ELSE IF ({Comando.plant} = "APD") THEN "APODACA"
ELSE IF ({Comando.plant} = "SAL") THEN "SALTILLO"
ELSE IF ({Comando.plant} = "LIN") THEN "LINDA VISTA"
ELSE IF ({Comando.plant} = "AER") THEN "AEROPUERTO"
ELSE IF ({Comando.plant} = "RIO") THEN "LA RIOJA"
ELSE " "
```

Se rompe cada vez que abre una planta nueva. **Reemplazo:** usar
`Erp.BO.PlantSvc/Plants` → campo `Name`, que ya consultamos en
`OrganizationService.cs` para el selector de planta — es exactamente el
mismo dato ("LA FE" para el plant `LAF`, que concatenado con el nombre de
compañía da "CARNES FINAS SAN JUAN LA FE", el título que se ve en el PDF de
ejemplo). Dinámico para cualquier planta sin tocar código.

### 2.3 El correo del pie de página — reemplazo dinámico

Fórmula actual del `.rpt` (usada en el pie, junto al correo fijo de Elvira):

```
IF ({Comando.plant} = "STC") THEN "Compras.santacatarina@carnessanjuan.com"
ELSE IF ({Comando.plant} = "APD") THEN "Compras.apodaca@carnessanjuan.com"
ELSE {Comando.COUNTRY}
```

Mismo problema (incompleta, ya lo sabe el usuario) y mismo tipo de arreglo,
pero el dato correcto **no es un correo fijo por planta** — es el correo del
**usuario que tiene la sesión abierta**, capturado en Epicor en *User
Account Maintenance* (`Ice.UserFile`, campo `Email`, confirmado visualmente
en Epicor para el usuario `epicor`:
`giovanni.montoya@carnessanjuan.com`). Se resuelve con
`Ice.BO.UserFileSvc/UserFiles?$filter=UserID eq '{session.Username}'`,
seleccionando el campo de correo (nombre exacto —`EMailAddress` o
`Email`— a confirmar contra Swagger antes de codificar, siguiendo la
disciplina de todo este proyecto).

### 2.4 Cola de correo — `interfaz_envia_oc`

`sp_EnviaOC_V2` vive en `ProdCFSJ`, en el mismo SQL Server que usa Epicor
(`SRVCSJPR2`), y su única lógica es este `INSERT` por *linked server*:

```sql
ALTER PROCEDURE [dbo].[sp_EnviaOC_V2] (
    @company varchar(20), @companyName varchar(500),
    @plant varchar(20), @plantName varchar(500),
    @poNumber varchar(50), @vendorID varchar(50), @vendorName varchar(500),
    @emails varchar(max), @oc_tipo int
)
AS
BEGIN
    INSERT INTO [192.168.100.18].CFSJService.dbo.interfaz_envia_oc(
        company, companyName, plant, plantName, poNumber, vendorID,
        vendorName, emails, fechaInserto, fechaUltimaRevision, fechaProceso,
        estatus, mensaje, oc_tipo)
    SELECT @company, @companyName, @plant, @plantName, @poNumber, @vendorID,
           @vendorName, @emails, GETDATE(), NULL, NULL, 0, '', @oc_tipo
    SELECT 1 'OK'
END
```

El código C# que la invoca (`EnviaServiceOC`) construye el `EXECUTE` con
`String.Format` — el vector de inyección que el spec original marcó como
defecto (10.8). Confirmado por el usuario, con acceso directo por SSMS a
`192.168.100.18` (tabla `CFSJService.dbo.interfaz_envia_oc`, credenciales SQL
ya provistas — ver `appsettings.Development.json`, no versionado): como el SP
no tiene ninguna lógica más que ese `INSERT`, **la app va a insertar
directo en `interfaz_envia_oc` en `192.168.100.18`**, sin pasar nunca por
`SRVCSJPR2` ni por el SP — consistente con que el resto de este proyecto
evita tocar la base de datos de Epicor por SQL directo.

Esquema confirmado de la tabla (columnas reales vistas en SSMS):
`id_envia_oc` (identity), `company`, `companyName`, `plant`, `plantName`,
`poNumber`, `vendorID`, `vendorName`, `emails` (string, correos separados por
`;`), `fechaInserto`, `fechaUltimaRevision`, `fechaProceso`, `estatus`,
`mensaje`, `oc_tipo`.

### 2.5 `oc_tipo` — significado real

El botón legacy `btnEnviarEmail_Click` ("Enviar OC por email al usuario")
**no manda nada al proveedor** — solo reenvía la OC al correo del
comprador logueado:

```csharp
private void btnEnviarEmail_Click(object sender, System.EventArgs args) {
    // ...
    var emailUsuario = GetEmailUser();
    if (!ValidaEmail(emailUsuario)) {
        MessageBox.Show("La direccion de email del usuario no esta capturada "
            + "correctamente, no se puede enviar el email.", ...);
        return;
    }
    EnviaServiceOC(compañia, GetCompanyName(compañia), planta,
        GetPlantName(compañia, planta),
        (string)ugOCS.ActiveRow.Cells["VendorVendorID"].Value,
        (string)ugOCS.ActiveRow.Cells["VendorName"].Value,
        ((int)ugOCS.ActiveRow.Cells["PONum"].Value).ToString(),
        2, // oc_tipo
        new[] { emailUsuario }.ToList());
}
```

Esto confirma: `oc_tipo = 2` es "copia al usuario" (un solo destinatario,
el propio comprador), `oc_tipo = 1` (fuera de alcance, ver sección 1) es el
envío automático al proveedor con los correos de `Vendor_UD`.

## 3. Backend — Generación del PDF

**Endpoint:** `GET /api/purchase-orders/{poNum}/report` → `application/pdf`,
`Content-Disposition: inline`. El frontend ya llama a la API con
`credentials: 'include'` (cookies de sesión), así que abrir esta URL en una
pestaña nueva (`window.open`) manda la cookie automáticamente — no se
necesita ningún token adicional.

**Servicio nuevo:** `PurchaseOrderReportService`, que junta:

- **Header + líneas** — reutiliza el patrón ya probado de
  `PurchaseOrderHistoryService.GetDetailAsync`:
  `Erp.BO.POSvc/POes('{Company}',{PONum})?$expand=PODetails`. Esta llamada
  **no** usa `$select`, así que ya trae todos los campos del header sin
  costo adicional (confirmado en vivo: `Approve`, `ApprovalStatus`,
  `BuyerIDName`, `ShipAddress1-3`, `ShipCity`, `ShipState`, `ShipZip`,
  `ShipViaCode`, `TermsCode`, `CommentText`, `DocTotalTax`, `TotalWhTax`,
  `CurrencyCode`, etc. — solo falta ampliar el DTO para leerlos).
- **Releases** — `Erp.BO.POSvc/PORels?$filter=PONum eq {poNum}` (ya la
  usamos), de aquí sale `ReqNum` por línea si aplica.
- **Dirección del proveedor** — `Erp.BO.VendorSvc/Vendors` (`Address1`,
  `Address2`, `City`).
- **Dirección de entrega (fallback) + RFC** — `Erp.BO.CompanySvc`
  (`Address1/2`, `City`, `State`, `Zip`, `StateTaxID`), más teléfono de
  `Erp.BO.PlantSvc/Plants`.
- **Nombre de planta ("SUCURSAL")** — `Erp.BO.PlantSvc/Plants` → `Name`
  (sección 2.2).
- **Descripción de "Entrega"** — `Erp.BO.ShipViaSvc` vía `ShipViaCode`
  (sección 2.1).
- **Correo del comprador (pie de página)** — `Ice.BO.UserFileSvc/UserFiles`
  filtrado por `session.Username` (sección 2.3).
- **EAN** — ya viene en la respuesta si se agrega a `PartPC` vía la relación
  de `PODetail`; a confirmar en vivo si el `$expand` anidado lo trae o hace
  falta una llamada aparte a `Erp.BO.PartSvc` con el filtro `PCType='EAN-13'`.

Todos los nombres de campo exactos de Company/PurAgent/ShipVia/Plant-teléfono
se verifican contra Swagger antes de escribir el código final del plan de
implementación — igual que se hizo con `PODetails` en el bug de la sesión
anterior.

**Sello de aprobación:** `"APROBADA"` si `Approve = true`, si no
`"NO APROBADA"`.

**Layout (QuestPDF)** — replica el PDF de ejemplo (`ejmploOC.pdf`, OC 3431):

- **Primera página únicamente:** logo, título "ORDEN DE COMPRA", nombre de
  compañía+planta, "SUCURSAL:", sello Aprobada/No Aprobada, "Orden de
  Compra: {PONum}", "N° Requisicion:", los dos recuadros lado a lado
  (Proveedor / Domicilio de Entrega), y el recuadro Entrega+Fecha de Orden.
- **Tabla de líneas** — fluye entre páginas: Línea, Parte\Rev\Descripción,
  EAN, Cantidad, UOM, Precio Unit, Precio Ext (agrega etiquetas claras a
  Cantidad/UOM, que en el `.rpt` original no tienen encabezado visible —
  mejora menor, sin perder ningún dato).
- **Al final de la tabla:** Autorizado por / Solicitado Por (izquierda),
  Subtotal Línea(s) / Impuestos / Retenciones / Subtotal de Cargos / Total
  (derecha), luego Comentarios y Cambios Físicos.
- **Pie de página, en cada página** (es literalmente la sección "Page
  Footer" del `.rpt`, confirmado por el usuario con una captura del
  diseñador de Crystal): nombre de compañía a la izquierda, "Página N de M"
  a la derecha, y las 4 líneas fijas de avisos fiscales con el correo del
  comprador (sección 2.3) + el correo fijo de Elvira Reyes.

## 4. Backend — Enviar copia al usuario

**Endpoint:** `POST /api/purchase-orders/{poNum}/send-copy` (sin body).

**Flujo:**

1. Resuelve el correo del usuario logueado (`Ice.BO.UserFileSvc`, sección
   2.3).
2. Valida el formato del correo; si no es válido, `400` con un mensaje claro
   — mismo criterio que el legacy `ValidaEmail`.
3. Junta `companyName` (de `session.companies`), `plantName` (de
   `Erp.BO.PlantSvc/Plants`), y `vendorID`/`vendorName` del header de la OC.
4. Inserta directo en `interfaz_envia_oc` en `192.168.100.18` con
   `Microsoft.Data.SqlClient`, **parámetros tipados** (nunca concatenación —
   corrige el defecto 10.8 del spec original):
   `company`, `companyName`, `plant`, `plantName`, `poNumber`, `vendorID`,
   `vendorName`, `emails = {correoDelUsuario}`, `oc_tipo = 2`,
   `fechaInserto = GETDATE()`, `fechaUltimaRevision = NULL`,
   `fechaProceso = NULL`, `estatus = 0`, `mensaje = ''`.
5. `200` con confirmación, o error descriptivo si el insert falla.

**Nota de alcance:** la columna "OC Enviada" del grid de "OCs por proveedor"
se queda como placeholder estático (`"Pendiente"`) — conectarla al `estatus`
real de `interfaz_envia_oc` requeriría entender qué significan los demás
valores de `estatus`, que el spec original ya marcó como una caja negra sin
bloquear (`CFSJService`, contenido desconocido). Se deja para un plan
futuro.

## 5. Frontend

- **"Visualizar OC"** — deja de estar deshabilitado; `onClick` hace
  `window.open('/api/purchase-orders/{poNum}/report', '_blank')`.
- **"Enviar OC por email al usuario"** — deja de estar deshabilitado;
  `onClick` llama a `api.purchaseOrders.sendCopy(poNum)`
  (`POST .../send-copy`), muestra éxito o el mensaje de error tal cual lo
  devuelva el backend, y se deshabilita mientras la petición está en curso
  (mismo patrón que `PurchaseOrderPanel`).
- Ambos siguen requiriendo una fila seleccionada (`selectedPoNum !== null`),
  como ya está la UI.

## 6. Pruebas

- **Backend:** unitarias para `PurchaseOrderReportService` (mapeo de campos,
  sello Aprobada/No Aprobada, fallback de dirección Ship→Company) con un
  stub de `IEpicorClient`; unitarias para el endpoint de envío (validación
  de correo, construcción del `INSERT` parametrizado) con un stub/fake de la
  conexión SQL — nunca contra `192.168.100.18` real en pruebas automatizadas.
- **Frontend:** verificación manual en el navegador (abrir el PDF real de
  una OC, enviar la copia a un correo real) — consistente con que este
  proyecto no tiene pruebas automatizadas de UI todavía.

## 7. Defectos corregidos

- **10.8 (parcial):** SQL concatenado en el envío de correo → parámetros
  tipados vía `Microsoft.Data.SqlClient`. (El otro caso de 10.8,
  `GetVendorEmails`, no aplica: ese flujo quedó fuera de alcance en la
  sección 1.)
- **Fragilidad de "SUCURSAL" y del correo del pie de página:** los dos
  `IF/ELSE` fijos por planta (incompletos y que rompen con cada sitio nuevo)
  se reemplazan por datos dinámicos ya disponibles en Epicor
  (`Plant.Name`, correo del usuario logueado).
