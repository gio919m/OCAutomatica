# OC Automática — Migración a aplicación web

**Fecha:** 2026-07-20
**Estado:** Diseño aprobado, pendiente de plan de implementación

---

## 1. Contexto

Carnes Finas San Juan actualizó Epicor Kinetic a la versión 2026, que se accede
únicamente por navegador. La customización **OC Automática**, escrita en C# sobre
el cliente de escritorio de Epicor, deja de ser viable y debe migrarse.

La pantalla la usa el área de compras. Su función: seleccionar un proveedor, ver
los productos que ese proveedor surte junto con inventario y costo, marcar los
que se van a pedir con sus cantidades, y generar una orden de compra aprobada en
Epicor que se envía por correo al proveedor en formato PDF.

### Sistema actual

| Componente | Implementación |
|---|---|
| Interfaz | Windows Forms embebido en el cliente Epicor (UltraGrid) |
| Datos del grid | SQL directo a `ProdCFSJ` con usuario `sa` |
| Creación de OC | `Erp.Adapters.POAdapter` con secuencia de llamadas a BO |
| Reporte | Crystal Reports (`\\192.168.100.81\Reports\ReporteOC.rpt`) |
| Envío de correo | `sp_EnviaOC_V2` inserta en cola en servidor `192.168.100.18` |

---

## 2. Objetivo

Reemplazar la customización por una aplicación web independiente que:

- Se acceda desde el navegador, enlazada desde el menú de Epicor
- Conserve el flujo de trabajo que los compradores ya conocen
- Elimine el acceso directo a base de datos y las credenciales incrustadas
- Corrija los defectos identificados en el código actual (sección 10)
- Sea transaccional: nunca deje órdenes creadas a medias

### Fuera de alcance

- **Edición de órdenes existentes.** Corporativo modifica las OC directamente en
  Epicor. La aplicación solo crea y consulta.
- **Reemplazo del servicio de correo.** La cola `sp_EnviaOC_V2` y el servicio
  `CFSJService` se conservan tal cual.
- **Cierre de Cambios Físicos.** Prevención sigue siendo dueña de ese ciclo; la
  aplicación solo lee.

---

## 3. Arquitectura

```
┌─────────────────────────────────────────┐
│  Navegador — React 18 + TypeScript      │
└────────────────┬────────────────────────┘
                 │ HTTPS · cookie httpOnly
┌────────────────▼────────────────────────┐
│  Backend — ASP.NET Core 8 Web API       │
│  ├─ Auth       (sesión, company/plant)  │
│  ├─ Vendors    (proveedores, correos)   │
│  ├─ Parts      (grid)                   │
│  ├─ PurchaseOrders (crear, consultar)   │
│  ├─ Reports    (PDF)                    │
│  └─ EmailQueue (encolar envío)          │
└──────┬──────────────────────────┬───────┘
       │ REST v2                  │ SQL parametrizado
┌──────▼──────────────────┐  ┌────▼─────────────────┐
│  Epicor Kinetic 2026    │  │  sp_EnviaOC_V2       │
│  · BAQs                 │  │  → CFSJService       │
│  · Function OCA_CrearOC │  │    (192.168.100.18)  │
│  · BO POSvc, BuyerSvc   │  └──────────────────────┘
└─────────────────────────┘
```

**Principio rector: el navegador nunca habla con Epicor ni con SQL.** Solo con la
API propia. Ninguna credencial, cadena de conexión o nombre de tabla llega al
cliente.

### Por qué una Epicor Function

El código actual ejecuta esta secuencia desde el cliente:

```
GetNewPOHeader → ChangeVendor → [por cada línea:
    GetNewPODetail → PartStatusValidationMessages → ChangeDetailPartNum →
    ChangeDetailCalcOurQty → ChangeUnitPrice → Update]
→ ChangeApproveSwitch → Update
```

`POAdapter` mantenía el dataset en memoria del cliente. En REST v2 cada llamada
es sin estado: se envía el dataset completo y se recibe modificado. Replicar esta
coreografía desde el backend significa ~160 llamadas HTTP para 20 artículos,
arrastrando el dataset entero en cada una.

Trasladar la secuencia a una **Epicor Function** (`OCA_CrearOC`) resuelve tres
problemas de una vez:

1. Una sola llamada de red desde la API
2. **Transaccionalidad**: si falla la línea 12, no queda una OC mutilada y aprobada
3. La lógica de negocio queda versionada dentro del ERP

El código C# de la Function es prácticamente el mismo que hoy vive en
`btnProc_Click`, sin la parte de interfaz.

---

## 4. Stack tecnológico

### Frontend
- **React 18 + TypeScript** — Vite como bundler
- **AG Grid** — celdas editables, checkboxes, virtualización
- **TanStack Query** — caché y estado de servidor

### Backend
- **ASP.NET Core 8** Web API
- **QuestPDF** — generación del PDF (licencia Community, gratuita bajo 1 MDD de ingresos)
- **Serilog** — log estructurado
- `Microsoft.Data.SqlClient` — únicamente para encolar el correo

### Infraestructura
- **IIS sobre Windows Server 2019** con ASP.NET Core Hosting Bundle
- Uso interno en LAN, HTTPS con certificado de la CA del dominio

---

## 5. Autenticación y sesión

### El problema del contexto perdido

El código actual obtiene compañía y planta del contexto de Kinetic:

```csharp
EpiDataView ContextData = (EpiDataView)oTrans.EpiDataViews["CallContextClientData"];
string Compañia = ContextData.dataView[ContextData.Row]["CurrentCompany"].ToString();
string Planta   = ContextData.dataView[ContextData.Row]["CurrentPlant"].ToString();
```

Una aplicación web externa no hereda ese contexto. Compañía y planta atraviesan
todo el sistema: filtran inventario por almacén, filtran Cambios Físicos y
determinan la dirección de entrega del reporte.

### Diseño

**Login.** El usuario captura sus credenciales de Epicor. El backend las valida
llamando a Epicor con ellas. No existe tabla de usuarios propia ni contraseñas
duplicadas: dar de baja a alguien en Epicor le retira el acceso automáticamente.

**Sesión.** Credenciales cifradas del lado servidor. El navegador solo recibe una
cookie `httpOnly` + `Secure` + `SameSite=Strict` con el identificador de sesión.
Las credenciales nunca llegan al cliente. Expiración por inactividad: 8 horas
(cubre un turno).

**Compañía y planta.** Al entrar, el backend consulta las compañías y plantas
autorizadas para ese usuario y presenta un selector. La selección vive en la
sesión, se muestra en el encabezado y se recuerda entre sesiones.

### Resolución del BuyerID

El código actual usa un respaldo fijo:

```csharp
if (ds.POHeader[0]["BuyerID"].ToString()=="") ds.POHeader[0]["BuyerID"] = "LNC-CM2";
```

Los BuyerID están segmentados por sucursal (`LAF-CM5`, `SDO-CM1`, `LNC-CM2`), por
lo que este respaldo puede atribuir una orden a un comprador de otra compañía.

Epicor ya tiene el modelo correcto en **Buyer Maintenance**:

- Cada buyer tiene un usuario marcado como **Default Buyer** — su dueño
- **Authorized Users** lista quienes pueden modificar sus órdenes (usado por
  corporativo, fuera del alcance de esta aplicación)

**Regla:** el BuyerID de una orden nueva es aquel donde el usuario de la sesión
está marcado como Default Buyer, dentro de la compañía activa. Si no tiene
ninguno, la aplicación **bloquea la creación** con un mensaje explícito en lugar
de inventar un comprador.

Se consulta vía `Erp.BO.BuyerSvc`, que expone `PurAgent` y su tabla hija de
usuarios autorizados.

---

## 6. Objetos a crear en Epicor

| Objeto | Tipo | Reemplaza a |
|---|---|---|
| `OCA_PartesPorProveedor` | BAQ | Query de `GetDataIntoDT` |
| `OCA_DatosReporteOC` | BAQ | Query embebido en `ReporteOC.rpt` |
| `OCA_CorreosProveedor` | BAQ | `GetVendorEmails` |
| `OCA_CambiosFisicos` | BAQ | Query a `ice.UD104/UD104A` (solo previsualización) |
| `OCA_CrearOC` | Function | `btnProc_Click` |

**Sobre los Cambios Físicos hay dos consumidores distintos:**

- El **BAQ** sirve a la interfaz, para mostrarle al comprador los cambios
  pendientes de ese proveedor **antes** de procesar. Hoy no los ve: se entera de
  que existen cuando ya salieron impresos en la orden.
- La **Function** los consulta por su cuenta al construir el `CommentText`, dentro
  de la misma transacción. No depende del BAQ ni de lo que haya visto la interfaz,
  de modo que el comentario refleja el estado al momento de crear la orden.

### OCA_PartesPorProveedor

Parámetros: `company`, `plant`, `vendorID`.

Columnas: Proveedor, No. Parte, Descripción, EAN-13, Mínimo, Máximo, Inventario,
Costo, Cantidad a Surtir (0), Asignar (false), ID Proveedor, Cantidad en Tránsito,
UOM, EAN-14.

Traducción del SQL original:
- **Inventario** — suma de `PartBin.OnHandQty` en los almacenes de la planta
  (`Warehse` filtrado por `Plant`)
- **Cantidad en Tránsito** — suma de `PODetail.XOrderQty` para órdenes con
  `POHeader.OpenOrder = 1 AND Approve = 1`, unidas por `PORel` filtrado por planta
- **EAN-13 / EAN-14** — dos joins a `PartPC` por `PCType`, coincidiendo
  `Part.IUM = PartPC.UOMCode`
- **Costo** — `VendPart.BaseUnitPrice`
- **Mínimo / Máximo** — `PartPlant.MinimumQty` / `MaximumQty`
- Filtro heredado: `VendPart.EffectiveDate >= '2021-01-01'`

Los CTEs del SQL original se expresan como subqueries en el BAQ.

### OCA_DatosReporteOC

Parámetros: `company`, `poNum`. Datos para el PDF.

Detalles heredados del query de Crystal que deben preservarse:

- **Domicilio de entrega**: si `POHeader.ShipAddress*` está vacío, usa la
  dirección de `Erp.Company`; concatena teléfono de `Erp.Plant` y
  `Company.StateTaxID` como RFC
- **Comprador**: `PurAgent.Name` vía `POHeader.BuyerID`
- **Requisitor**: `UserFile.Name` vía `ReqHead.RequestorID`
- **Comentarios**: `POHeader.CommentText`
- **Estado de aprobación**: `POHeader.Approve` / `ApprovalStatus` determinan el
  sello "NO APROBADA"

### OCA_CrearOC (Function)

**Entrada:**
```
company, plant, vendorID, buyerID, comentarios,
lineas[] { partNum, cantidad, costo, uom }
```

**Salida:** `poNum`, o error descriptivo.

**Proceso (una transacción):**
1. `GetNewPOHeader`, asignar `BuyerID` recibido
2. `ChangeVendor(vendorID)`
3. Consultar Cambios Físicos pendientes del proveedor y construir `CommentText`
4. `Update` → obtener `PONum`
5. Por cada línea: `GetNewPODetail`, `PartStatusValidationMessages`,
   `ChangeDetailPartNum`, asignar `PartNum`/`PUM`/`CurrencySwitch=false`/
   `RowMod="A"`, `ChangeDetailCalcOurQty`, `ChangeUnitPrice`, `Update`
6. Asignar `Approve = true`, `ApprovalStatus = "A"`, `Unlock_c = true`
7. `ChangeApproveSwitch(true)`, `Update`

**Nota:** el `BuyerID` se recibe ya resuelto desde la API, no se calcula dentro
de la Function.

---

## 7. Funcionalidad

### Pantalla principal

Se conserva la disposición actual: panel izquierdo con proveedor, comentarios y
correos; grid a la derecha. Los compradores llevan años con esta pantalla y no
hay razón para hacerlos reaprenderla.

**Flujo:**

1. **Seleccionar proveedor** — por código o desde el combo
2. **Se llena el grid** — llamada a `OCA_PartesPorProveedor`
3. **Marcar y capturar cantidades** — "Asignar" habilita la celda de cantidad
4. **Validar** — cantidad > 0 y costo > 0, evaluados como decimales
5. **Procesar** — una llamada a `OCA_CrearOC`, luego encolar correo

**Mejoras de comportamiento:**

- **Búsqueda no destructiva.** El filtro es visual; las filas marcadas siguen
  contando aunque no se vean. Se muestra siempre el conteo de artículos
  seleccionados. Esto elimina la necesidad del parche actual que limpia el filtro
  antes de procesar.
- **Validación en línea.** Los errores se marcan en la celda mientras se captura,
  no en un diálogo al final. El botón Procesar se deshabilita con el motivo visible.
- **Virtualización.** Proveedores con cientos de partes se desplazan sin retraso.
- **Separación de orden y correo.** La orden se confirma por su cuenta; si falla
  el encolado del correo, queda un botón de reintento sobre esa OC.

### Pestaña "OCs por proveedor"

Solo consulta. Tres acciones:

- **Actualizar** — refresca la lista
- **Visualizar OC** — genera y muestra el PDF
- **Enviar OC por email** — vuelve a encolar el envío

La columna "OC Enviada" refleja el `estatus` de la cola de correo.

### Cambios Físicos

Producto que el proveedor debe reponer por haberse echado a perder. Prevención lo
registra en `UD104` y el aviso viaja en el comentario de la OC para que el
proveedor traiga las reposiciones.

Consulta: registros con `character06 = 'PENDIENTE'` para el proveedor, compañía y
planta activos, agrupados y concatenados en el texto `"Cambios Fisicos: ..."`.

**Los registros permanecen en PENDIENTE hasta que el proveedor entrega**;
prevención los cierra manualmente. La aplicación solo lee, nunca escribe.

---

## 8. Generación del PDF

Crystal Reports no tiene ruta soportada hacia .NET moderno multiplataforma, y el
`.rpt` actual contiene su propia consulta SQL conectándose con `sa`.

**Reemplazo: QuestPDF**, generando desde `OCA_DatosReporteOC`. Un solo código
sirve para visualizar en el navegador y para adjuntar al correo.

Elementos a reproducir del formato actual:

- Logo de Carnes Finas San Juan
- Encabezado: número de OC, fecha de impresión, sucursal, sello de aprobación
- Bloque de proveedor (izquierda) y domicilio de entrega (derecha)
- Datos de entrega y fecha de orden
- Tabla de líneas: Línea, Parte/Rev/Descripción, EAN, cantidad, UOM, precio
  unitario, precio extendido
- Totales: subtotal de líneas, impuestos, retenciones, subtotal de cargos, total
- Autorizado por / Solicitado por
- Comentarios y Cambios Físicos
- Pie de página fijo con las leyendas fiscales y de contacto

---

## 9. Envío de correo

Se conserva la arquitectura actual. `sp_EnviaOC_V2` inserta en
`[192.168.100.18].CFSJService.dbo.interfaz_envia_oc` con `estatus = 0`, y el
servicio `CFSJService` procesa la cola.

**Único cambio: parámetros tipados en lugar de SQL concatenado.** El código actual
construye el `EXECUTE` con `String.Format`, lo que hace fallar el envío ante
cualquier nombre de proveedor con apóstrofe y constituye un vector de inyección
(la lista de correos es editable por el usuario).

**Destinatarios:** los campos UD de `Vendor_UD` (`ud_Correo1_c` … `ud_Correo3_c`),
más los agregados manualmente en la interfaz. El BAQ elimina el tope rígido de
tres correos del código actual.

---

## 10. Defectos corregidos

Identificados durante la revisión del código actual.

### 10.1 La validación de cantidad rechaza decimales

```csharp
if ((Convert.ToBoolean(R2.Cells[9].Value)==true) &&
    (Convert.ToInt32(R2.Cells[8].Value)<=0 || Convert.ToDecimal(R2.Cells[7].Value)<=0))
```

`Cells[8]` es `Double`, pero se valida convirtiendo a `Int32`. `Convert.ToInt32`
redondea: 0.5 → 0, 0.4 → 0. **Cualquier cantidad menor a 0.5 kg es rechazada** con
el mensaje de "cantidad menor o igual a cero". Peor: con 2.5 la validación evalúa
2 pero la línea se crea con 2.5, porque `newCalcOurQty` usa `Convert.ToDecimal`.
Validación y ejecución no coinciden.

**Corrección:** validar como decimal, consistente con la ejecución.

### 10.2 Desalineación del índice con filas ocultas

```csharp
contador++;
if (!R.Hidden) { ... ds.PODetail[contador] ... }
```

El incremento está fuera del `if`. Una fila marcada pero oculta por el filtro
avanza el contador sin crear el `PODetail`, y el siguiente acceso se sale de
rango. Hoy lo evita este parche al inicio del método:

```csharp
if(txtBuscar.Text != ""){ txtBuscar.Text = ""; FilterDgProveedores(...); }
```

**Corrección:** el modelo de datos del frontend no depende de la visibilidad de
las filas. El parche deja de ser necesario.

### 10.3 Ausencia de transacción

Si la creación falla a media secuencia, queda una OC parcial y aprobada en Epicor
sin posibilidad de reversión automática.

**Corrección:** la Function ejecuta todo en una transacción.

### 10.4 BuyerID fijo

`"LNC-CM2"` como respaldo puede atribuir órdenes a un comprador de otra sucursal.

**Corrección:** resolución desde Buyer Maintenance; bloqueo si no hay buyer válido.

### 10.5 Fuga de conexión SQL

En el query de Cambios Físicos, `SqlConnection conectar` se abre sin `using` y
nunca se cierra.

**Corrección:** desaparece la conexión directa.

### 10.6 CommentText asignado dos veces

Se asigna con el texto de Cambios Físicos y más abajo se sobrescribe con
`AsignarValoresATextBox(vendID)`. La primera asignación se pierde.

**Corrección:** una sola construcción del comentario. *(Pendiente de confirmar
revisando `AsignarValoresATextBox` — ver sección 12.)*

### 10.7 Errores silenciados

```csharp
}catch(Exception ex){}
```

En `GetVendorEmails`, un fallo de red se le presenta al usuario como "el proveedor
no tiene email's capturados... deberá asignar los email's en el catálogo de
proveedores", culpando al catálogo por un problema de infraestructura.

**Corrección:** log estructurado y mensaje que distingue "no hay correos
capturados" de "no se pudieron consultar los correos".

### 10.8 SQL concatenado

En `sp_EnviaOC_V2` y `GetVendorEmails`. Rompe con apóstrofes y permite inyección.

**Corrección:** parámetros tipados y BAQs.

---

## 11. Seguridad

| Hoy | Después |
|---|---|
| `sa`/`Epicor123` en `GetDataIntoDT` | BAQ vía REST |
| `sa`/`Epicor123` en `ReporteOC.rpt` | QuestPDF sobre BAQ |
| Cadenas de conexión en la customización | Configuración cifrada del servidor |
| SQL concatenado (dos lugares) | Parámetros tipados |
| Errores silenciados | Log estructurado |

**Acción independiente y urgente: rotar la contraseña de `sa`.** Está en el código
de la customización, dentro de un `.rpt` en un share de red, y presumiblemente en
respaldos y copias del proyecto. Cualquiera con acceso al código tiene control
total de `ProdCFSJ`. No debe esperar a que termine esta migración.

**HTTPS obligatorio.** La aplicación recibe credenciales de Epicor; en HTTP plano
son legibles por cualquiera en la red.

---

## 12. Supuestos y pendientes

| Punto | Estado |
|---|---|
| `AsignarValoresATextBox` | No revisado. Confirmar si el primer bloque de Cambios Físicos es código muerto. |
| Contenido de `CFSJService` (192.168.100.18) | Desconocido. No bloquea: la cola se conserva sin cambios. |
| Alcance de red | **Confirmado:** uso exclusivo en la red interna. Sin exposición a internet. |
| Diseño exacto del `.rpt` | Se reconstruye desde el PDF de ejemplo. Si hay variantes de formato no visibles, ajustar. |
| `oc_tipo` en `sp_EnviaOC_V2` | Hoy siempre `1`. Verificar si existe otro valor para reenvíos. |
| Botón "Abrir manual de usuario" | No analizado. Definir destino en la versión web. |
| Origen del combo de proveedores | No analizado. Presumiblemente `Erp.Vendor` filtrado por compañía. |

---

## 13. Criterios de aceptación

1. Un comprador inicia sesión con sus credenciales de Epicor y selecciona
   compañía y planta.
2. Al elegir un proveedor, el grid muestra los mismos datos que la pantalla actual.
3. Se pueden capturar cantidades decimales (0.5 kg se acepta).
4. La búsqueda no descarta las selecciones hechas previamente.
5. Al procesar, se crea en Epicor una OC **aprobada** con el BuyerID del usuario.
6. Si la creación falla, no queda ninguna OC parcial.
7. Los comentarios y los Cambios Físicos pendientes aparecen en la OC.
8. Se encola el envío de correo a los destinatarios del proveedor más los
   agregados manualmente.
9. El PDF generado es equivalente al reporte de Crystal actual.
10. Un usuario sin buyer propio no puede crear órdenes y recibe un mensaje claro.
11. Ninguna credencial de base de datos existe en el código de la aplicación.
