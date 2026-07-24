# Plan 5 — Envío automático de la OC al proveedor al crear la orden

## 1. Alcance

Completa el envío por correo que quedó explícitamente fuera del Plan 4: el que ocurre **automáticamente** justo al crear la orden (`oc_tipo = 1`, correos del proveedor + del comprador), no el que el comprador dispara a mano desde "OCs por proveedor" (`oc_tipo = 2`, ya construido y verificado en vivo).

Incluye:

- El grid "Correos a los que se enviará la OC" (Tipo: PROVEEDOR / CREADOR / INCLUIR, Email) en la pantalla de "Nueva orden de compra", que se llena automáticamente al seleccionar proveedor.
- El campo "Incluir en correo a:" + botón "Añadir" para agregar un correo manual, con las mismas validaciones del legacy (formato, sin duplicados).
- El botón "Quitar" para retirar una fila manual (solo tipo INCLUIR, nunca PROVEEDOR/CREADOR), con confirmación.
- El envío automático al proveedor al terminar "Procesar orden de compra" con éxito — sin botón separado, igual que el legacy.

**Explícitamente fuera de alcance:**

- El tipo "COPIA" (`this.emailsCopy`) — confirmado en el código real como una lista que se declara vacía y nunca se llena en ningún lado (`private String[] emailsCopy = new string[]{ };`). Dato muerto, no se replica.
- Los dos `MessageBox` informativos que el legacy muestra justo antes de enviar ("el proveedor no tiene correos capturados..." / "se enviará a estos destinatarios..."). El grid persistente ya transmite la misma información sin necesidad de un popup modal adicional.

## 2. Contexto reunido en vivo (código real del legacy)

### 2.1 `GetListEmails` — cómo se arma el grid

```csharp
private List<EmailItem> GetListEmails(string vendorID){
    List<EmailItem> emails = new List<EmailItem>();
    if(!String.IsNullOrEmpty(vendorID)){
        var vendorEmails = GetVendorEmails(currentCompany, vendorID);
        emails.AddRange(vendorEmails.Distinct().Select(s => new EmailItem(){
            tipo = "PROVEEDOR", email = s, valido = true
        }));
    }
    var emailUsuario = GetEmailUser();
    if(ValidaEmail(emailUsuario))
        emails.Add(new EmailItem(){ tipo = "CREADOR", email = emailUsuario, valido = true });
    else
        emails.Add(new EmailItem(){ tipo = "CREADOR", email = "", valido = false });

    var emailsIncluir = GetUsuariosIncluir(); // this.emailsIncluir.Where(ValidaEmail)
    foreach(var e in emailsIncluir)
        emails.Add(new EmailItem(){ tipo = "INCLUIR", email = e, valido = true });

    emails.AddRange(this.emailsCopy.Distinct().Select(s => new EmailItem(){
        tipo = "COPIA", email = s, valido = true
    })); // emailsCopy siempre vacía — dato muerto, no se replica

    return emails;
}
```

`GetVendorEmails` (ya visto en el Plan 4, sección 2.4 del spec original) consulta `Vendor_UD.ud_Correo1_c/2_c/3_c` por SQL directo — en este plan se reemplaza por OData contra Epicor, igual que todo lo demás del proyecto.

Una fila con `valido = false` se resalta en rojo en el grid original (solo le puede pasar a CREADOR, ya que PROVEEDOR/INCLUIR/COPIA siempre se construyen con `valido = true`).

### 2.2 Botón "Añadir" (`btnIncluirAdd_Click`)

```csharp
var emailInc = txtEmailIncluir.Text;
if(!ValidaEmail(emailInc)){
    MessageBox.Show("... no tiene un formato de correo valido ...");
    return;
}
if(this.emailsIncluir.Exists(e => e.Trim().ToLower().Equals(emailInc.Trim().ToLower()))){
    MessageBox.Show("... ya existe en la lista ...");
    return;
}
this.emailsIncluir.Add(emailInc);
txtEmailIncluir.Text = "";
ReloadGridEmails(txt01.Text);
```

### 2.3 Botón "Quitar" (`btnIncluirEliminar_Click`)

```csharp
if(ugCE.ActiveRow == null || (String)ugCE.ActiveRow.Cells["tipo"].Value != "INCLUIR"){
    MessageBox.Show("No ha seleccionado un registro tipo [INCLUIR] para ser retirado de la lista.");
    return;
}
var email = (String)ugCE.ActiveRow.Cells["email"].Value;
if(MessageBox.Show($"¿Desea quitar la direccion de email [{email}] de la lista.", ..., YesNo) == DialogResult.No) return;
this.emailsIncluir.Remove(email);
ReloadGridEmails(txt01.Text);
```

### 2.4 El envío real, al final de `btnProc_Click` (después de aprobar/actualizar la OC)

```csharp
var vendorEmails = GetVendorEmails(psValorCompany, vendID);
if(vendorEmails == null || vendorEmails.Count == 0)
    MessageBox.Show("El proveedor no tiene emails capturados...");
else
    MessageBox.Show("Se enviara la OC en PDF al proveedor con estos destinatarios: ...");

if(EnviaServiceOC(psValorCompany, GetCompanyName(psValorCompany), psValorPlant, GetPlantName(psValorCompany, psValorPlant),
                  vendID, txt02.Text, ds.POHeader[0]["PONum"].ToString(), 1,
                  GetListEmails(txt01.Text).Where(w => w.valido).Select(s => s.email).ToList())){
    MessageBox.Show("Proceso completado");
} else {
    MessageBox.Show("Proceso completado, se registro la OC pero ¡el correo no pudo ser enviado!");
}
```

Puntos clave que esto confirma:

- El envío **vuelve a calcular** PROVEEDOR + CREADOR desde cero llamando a `GetListEmails` de nuevo — nunca confía en lo que quedó pintado en el grid.
- Solo se envían las filas con `valido = true` (esto excluye al CREADOR si su correo no es válido, pero **no bloquea** el envío al proveedor por eso).
- Si el proveedor no tiene correos, el envío **de todos modos continúa** con lo que sí sea válido (creador + incluidos) — no es un bloqueo, solo un aviso informativo.
- El envío ocurre **siempre**, automáticamente, sin un botón separado — es parte del mismo flujo de "Procesar".
- Si el envío falla, la OC ya creada **no se revierte** — solo se avisa.

## 3. Backend

Se extiende el servicio que ya existe de la Task 7 del Plan 4 (`IPurchaseOrderEmailService`/`PurchaseOrderEmailService`), reutilizando su misma infraestructura (`IEpicorClient`, `IUserDirectoryService`, `IOrganizationService`, `IEmailQueueRepository`):

- **`GetRecipientsAsync(company, vendorId, username, credentials, ct)`** → replica `GetListEmails` (sin COPIA): junta filas PROVEEDOR (correos del proveedor, ver 3.1) + una fila CREADOR (correo del usuario logueado vía `IUserDirectoryService`, `valido=false` si no está capturado). Se usa solo para llenar el grid — no envía nada.
- **`SendToVendorAsync(company, companyName, plant, vendorId, username, poNum, manualEmails, credentials, ct)`** → vuelve a calcular PROVEEDOR+CREADOR desde cero llamando a `GetRecipientsAsync` internamente (nunca confía en el estado del navegador — igual que el legacy), valida cada `manualEmails` (mismo regex que ya usa `SendCopyToUserAsync`, descarta silenciosamente los inválidos/duplicados en vez de fallar toda la operación — el legacy tampoco bloquea el envío al proveedor por un correo manual mal escrito, solo lo rechaza al momento de "Añadir"), junta todos los correos válidos (proveedor + creador-si-válido + manuales), los une con `;`, y los inserta en `interfaz_envia_oc` con `oc_tipo = 1` vía `IEmailQueueRepository` (mismo repositorio del Plan 4, sin cambios).

### 3.1 Correos del proveedor

```
Erp.BO.VendorSvc/Vendors?$filter=VendorID eq '{vendorId}'&$select=ud_Correo1_c,ud_Correo2_c,ud_Correo3_c
```

**A verificar en vivo contra Swagger antes de codificar el detalle final:** si Epicor expone estos campos UD directo sobre `Vendors` (como ya confirmamos que hace con otros campos `_c` en el header de `POes`) o si hace falta otra ruta. Cada valor no vacío que pase el mismo formato de correo (`ValidaEmail`) se agrega como una fila PROVEEDOR.

### 3.2 Endpoints nuevos en `PurchaseOrdersController`

- `GET /api/purchase-orders/email-recipients?vendorId=X` → `_emailService.GetRecipientsAsync(session.Company, vendorId, session.Username, credentials, ct)`. Devuelve `[{ tipo, email, valido }]`.
- `POST /api/purchase-orders/{poNum}/send-to-vendor`, body `{ vendorId, manualEmails }` → resuelve `companyName` igual que `SendCopy` ya lo hace (`session.AvailableCompanies`), llama a `_emailService.SendToVendorAsync(...)`. Se llama automáticamente desde el frontend justo después de que crear la OC tenga éxito — no es un endpoint que el usuario dispare directo con un botón.

## 4. Frontend

- Nuevo grid "Correos a los que se enviará la OC" (Tipo / Email) dentro de `PurchaseOrderPanel.tsx`, llenado con `GET .../email-recipients?vendorId=X` cada vez que cambia el proveedor (mismo patrón `useEffect` que ya usa `cambiosFisicos`). Filas con `valido=false` se muestran en rojo.
- Campo "Incluir en correo a:" + botón "Añadir": valida formato y duplicados **del lado del cliente**, sin ir al servidor (igual que el legacy) — agrega a un estado local `manualEmails: string[]`, mostrado como filas tipo INCLUIR.
- Botón "Quitar" por cada fila INCLUIR (nunca en PROVEEDOR/CREADOR) con `window.confirm` antes de borrar, mismo texto que el legacy.
- `manualEmails` vive en el estado de `PurchaseOrderPanel`, que ya se remonta completo por su `key={vendor.vendorId}` en `App.tsx` — a diferencia del legacy (que no limpia `emailsIncluir` entre proveedores), esto resetea la lista manual en cada cambio de proveedor. Es una mejora deliberada, no una omisión.
- Al tener éxito "Procesar orden de compra" (después de `api.purchaseOrders.create`), se llama automáticamente a `POST .../send-to-vendor` con el proveedor actual y `manualEmails`. Si falla, se muestra un mensaje aparte ("la orden se creó pero el correo no se pudo enviar") sin afectar el estado de éxito de la OC ya creada — igual que el legacy nunca revierte la orden por esto.

## 5. Pruebas

- Backend: unitarias para `GetRecipientsAsync` (proveedor con/sin correos capturados, creador válido/inválido) y `SendToVendorAsync` (combina las tres fuentes, filtra inválidos silenciosamente, une con `;`, usa `oc_tipo=1`, nunca lanza por un correo manual mal formado) — con stubs a mano de `IEpicorClient`/`IUserDirectoryService`/`IEmailQueueRepository`, mismo patrón que el resto del proyecto. Sin FluentAssertions ni librerías de mocking.
- Frontend: sin pruebas automatizadas (consistente con el resto del proyecto) — verificación manual: crear una OC real y confirmar en SSMS (`192.168.100.18`) que la fila en `interfaz_envia_oc` quede con `oc_tipo=1` y los correos correctos (proveedor + creador + cualquier manual agregado).
