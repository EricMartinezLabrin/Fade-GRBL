# Contrato ExpressJS para Comunidad FadeGRBL — API v1

Estado: contrato del cliente de escritorio implementado en este repositorio. El servidor
ExpressJS y despliegue Backblaze se desarrollan por separado. No hay servidor de producción
incluido. Todos los nombres JSON son sensibles a mayúsculas y usan camelCase.

## 1. Invariantes

- FadeGRBL no necesita cuenta para arrancar, editar archivos propios o controlar máquina.
  Solamente biblioteca personal y marketplace requieren cuenta.
- Biblioteca local separada por origen HTTPS y cuenta. Caché y sesión permiten trabajo
  sin conexión; un error de autenticación nunca envía comandos ni detiene una máquina.
- Cada cuenta recibe una cuota privada de **200 000 000 bytes**, sin vencimiento.
  Publicaciones y adquisiciones utilizan almacenamiento separado; no descuentan esa cuota.
- Toda publicación v1 es gratuita. No existen cobros, tarjetas, saldos o ampliaciones.
  Rechazar `price != 0` con `PAYMENTS_NOT_ENABLED`.
- Regla futura, todavía inactiva: publicaciones de pago activas <= floor(publicaciones
  gratuitas aprobadas, originales y disponibles / 5). Contar diseños, no descargas.
  Duplicados, borradores y retirados no cuentan; validación y transacción son del servidor.
- Los SVG adquiridos se entregan internamente a la aplicación, no mediante enlaces públicos
  ni botones de descarga. Protección local reduce redistribución casual; no es DRM inviolable.
- Se permite fabricar y vender productos físicos. No se permite redistribuir/revender el SVG
  ni exportarlo desde FadeGRBL. Licencia identificada por `physical-products-v1`.
  Conservar texto exacto aceptado y versión en registro de adquisición. Texto legal definitivo
  debe aprobarse antes del lanzamiento; no cambiar condiciones de adquisiciones existentes.

## 2. Transporte, identidad y errores

Origen configurado por operador, por ejemplo `https://comunidad.example.org`, **sin ruta**.
Cliente añade `/api/v1/`. Ese dominio es ejemplo, no configuración de fábrica.
HTTPS obligatorio; no redirecciones. JSON UTF-8; responder 200 con modelo salvo excepción
indicada. Creaciones pueden responder 201. Operaciones sin resultado responden 204.
No envolver respuestas exitosas en `data`.
Express debe admitir cuerpos JSON de hasta 32 MB en publicación (SVG escapado), aplicar
límites menores al resto y validar tamaño SVG UTF-8 por separado; los respaldos binarios
se transfieren directamente con URL S3 firmada, nunca como JSON base64 de petición API.

Toda operación salvo registro, login, refresh, logout y recuperación necesita:

```http
Authorization: Bearer <accessToken>
Content-Type: application/json; charset=utf-8
```

JWT u otro token opaco de acceso válido 15 minutos. Refresh token aleatorio de 256 bits,
rotación en cada uso, vencimiento de 30 días y revocación por sesión. Guardar su hash en DB.
Verificar propietario de cada recurso; ignorar cualquier accountId aportado por cliente
para autorización. Responder 404 para IDs privados ajenos. Roles administrativos verificados
en servidor, nunca deducidos del cliente. CORS no es autenticación.

Login y refresh devuelven sesión completa:

```json
{
  "user": {
    "id": "usr_01", "email": "persona@example.org", "emailVerified": true,
    "displayName": "Diseños Ana", "bio": "Diseños para corte", "avatarUrl": null
  },
  "accessToken": "opaque-access", "refreshToken": "opaque-refresh",
  "expiresAt": "2026-11-07T18:15:00Z",
  "libraryKey": "<base64 de 32 bytes aleatorios persistentes de esta cuenta>"
}
```

`libraryKey` se crea una vez por cuenta y se guarda cifrada con secreto del servidor.
Debe mantenerse idéntica entre sesiones, computadoras y recuperación de contraseña;
perderla hace irrecuperables respaldos cifrados. Nunca incluirla en logs, perfiles ni listas.
Solamente devolverla al propietario en login/refresh sobre TLS. Cliente guarda sesión con
DPAPI CurrentUser de Windows. No guarda contraseña. `server` es dato local añadido por app.

Error uniforme:

```json
{"error":{"code":"QUOTA_EXCEEDED","message":"No hay espacio privado suficiente.","requestId":"req_01"}}
```

| HTTP | code | Situación |
|---|---|---|
| 400 | VALIDATION_FAILED | Metadatos, cursor o contrato inválidos |
| 401 | AUTH_REQUIRED / TOKEN_EXPIRED / SESSION_REVOKED | Token ausente, vencido o revocado |
| 403 | EMAIL_NOT_VERIFIED / FORBIDDEN / CONTENT_BLOCKED | Correo, rol o adquisición bloqueada |
| 404 | NOT_FOUND | Recurso ausente o privado ajeno |
| 409 | VERSION_CONFLICT / UPLOAD_INCOMPLETE / IDEMPOTENCY_CONFLICT | Conflicto recuperable |
| 413 | FILE_TOO_LARGE | Tamaño superior a límite |
| 422 | SVG_INVALID / PAYMENTS_NOT_ENABLED / LICENSE_INVALID | Contenido o modalidad inválida |
| 429 | RATE_LIMITED | Límite de solicitudes, incluir Retry-After |
| 507 | QUOTA_EXCEEDED | Reserva privada no cabe |
| 503 | STORAGE_UNAVAILABLE | DB, cola o almacenamiento indisponible |

Cliente renueva sesión una vez al recibir 401 y repite operación; no hace bucles de login.
Respuesta fallida no elimina sesión local, proyectos ni trabajos activos.

### Paginación, filtros y límites

Listas: `?limit=30&cursor=<opaco>`. Limit 1..50, cursor vinculado a consulta y orden estable
por createdAt descendente e id. Respuesta:

```json
{"items":[],"nextCursor":null}
```

`q` busca por nombre, descripción y etiquetas, sin distinguir mayúsculas; `category` y
`designerId` filtran diseños. En diseñadores solo aplica `q`; ignorar filtros vacíos o
no aplicables. Categoría: texto normalizado de 1..80 caracteres. No requiere catálogo fijo.
Título: 1..120; descripción: hasta 4000; bio: hasta 2000; nombre público: 1..80;
etiquetas: máximo 20, cada una 1..40. Rechazar exceso, no truncar silenciosamente.

IDs de recursos del servidor son strings opacos de hasta 128 caracteres. Nunca convertirlos
directamente a paths de disco u objetos S3. Fechas ISO 8601 UTC; versiones enteras positivas.
No exponer correos en perfiles públicos; devolver email/emailVerified solo para `/me` y auth.

`Idempotency-Key` obligatorio en adquisiciones, creación/finalización de uploads,
publicaciones y reportes. Alcance cuenta+ruta+método. Misma clave y mismo cuerpo devuelven
resultado original; cuerpo diferente produce 409. Retener claves al menos 24 h. Adquisición
única por cuenta+diseño permanece idempotente después de ese plazo.

## 3. Cuentas y perfiles

| Método y ruta | Cuerpo JSON | Resultado |
|---|---|---|
| POST `/auth/register` | `{"email":"a@example.org","password":"...","displayName":"Ana"}` | 204, enviar verificación |
| POST `/auth/login` | `{"email":"a@example.org","password":"..."}` | Sesión completa |
| POST `/auth/refresh` | `{"refreshToken":"..."}` | Sesión completa, refresh rotado |
| POST `/auth/logout` | `{"refreshToken":"..."}` | 204, revocar sesión; idempotente incluso si vencida |
| POST `/auth/email/verify` | `{"token":"codigo-del-correo"}` | 204, verificar cuenta autenticada |
| POST `/auth/email/resend` | Sin cuerpo | 204, enviar verificación a cuenta autenticada |
| POST `/auth/password/forgot` | `{"email":"a@example.org"}` | Siempre 204; enviar código si existe cuenta |
| POST `/auth/password/reset` | `{"token":"codigo","password":"nueva"}` | 204, revocar sesiones remotas, conservar libraryKey |
| GET `/me` | — | User privado completo |
| PATCH `/me` | `{"displayName":"Ana","bio":"Corte y grabado"}` | User privado actualizado |
| GET `/me/quota` | — | Quota |
| GET `/designers?q=Ana` | — | Página de User públicos |
| GET `/designers/:id` | — | User público |

Contraseña: 12..128 caracteres, almacenar hash resistente a fuerza bruta (Argon2id),
nunca cifrado reversible. Tokens de correo/reset aleatorios, de un solo uso, hash en DB,
caducidad 24 h y 30 minutos respectivamente. Mail real es responsabilidad del backend.
Límites iniciales configurables: login 10 intentos/15 min por IP y cuenta;
reenvío/recuperación 3/h por cuenta y 20/h por IP. No revelar existencia de correo en errores.

Quota ejemplo:

```json
{"limitBytes":200000000,"usedBytes":24000000,"reservedBytes":1200000}
```

## 4. Biblioteca privada y Backblaze S3

DB guarda propietarios, metadatos y versiones; Backblaze guarda bytes. Bucket privado,
credenciales únicamente en servidor, SDK S3 configurado con endpoint y región de Backblaze.
Cuenta de servicio con permisos mínimos sobre prefijos usados; nunca repartir applicationKey.
Sin ACL pública. URLs firmadas duran 5 minutos. Contenido cifrado no debe convertirse a SVG
ni servirse como HTML. MIME `application/octet-stream`; no usar nombre suministrado como key.

Modelo LibraryEntry remoto:

```json
{
  "id":"lib_01","title":"Caja pequeña","tags":["cajas"],
  "description":null,"category":null,"updatedAt":"2026-11-07T18:00:00Z",
  "version":1,"acquisitionId":null,"designId":null
}
```

| Método y ruta | Cuerpo / respuesta |
|---|---|
| GET `/library` | Página de LibraryEntry del propietario |
| POST `/uploads` | Reserva y ticket PUT, formato inferior |
| POST `/uploads/:id/complete` | `{"title":"Caja","tags":["cajas"]}` → LibraryEntry remoto |
| DELETE `/uploads/:id` | 204; cancelar reserva no completada |
| POST `/library/:id/content` | Ticket GET para bytes cifrados |
| DELETE `/library/:id` | `{"version":1}` → 204; eliminar solo respaldo remoto |

Crear/actualizar respaldo se hace mediante protocolo de uploads; no añadir otra ruta de
escritura de objetos. `libraryId=null` crea; `libraryId` existente actualiza si coincide version.
Cliente guarda remoteId y version tras completar; contenido binario conserva ID local UUID N.

Reserva de creación:

```json
{"purpose":"library","clientEntryId":"<UUID local N>","libraryId":null,"version":0,"sizeBytes":1200000,
 "sha256":"<64 hex minúsculas>","contentType":"application/octet-stream"}
```

Ticket PUT / GET:

```json
{"id":"upl_01","url":"https://<endpoint-backblaze>/<objeto>?<firma>",
 "method":"PUT","headers":{"Content-Type":"application/octet-stream"}}
```

GET usa `method:"GET"`; headers vacíos salvo encabezados firmados requeridos. Cliente solo
acepta `Content-Type` y `x-amz-*`; no solicitar otros encabezados, cookies ni Authorization.
URL no redirige. Token de API nunca se transmite a Backblaze.

### Cuota y consistencia

1. Transacción DB bloquea fila de cuota y valida propiedad/versión antes de reservar.
   Índice único cuenta+clientEntryId impide duplicados si se pierde respuesta de confirmación.
   Si esa entrada ya existe y cliente no conoce remoteId/version, devolver VERSION_CONFLICT;
   usuario puede consultar nube y restaurar respaldo para reconciliar metadatos locales.
2. Contar **bytes nuevos completos** durante actualización, además de versión anterior;
   cuota temporal debe alcanzar para ambas copias. Máximo 100 000 000 bytes por respaldo.
3. Reservar exactamente sizeBytes durante 15 minutos. Confirmar requiere HEAD y validación
   del contenido mediante SHA-256 real; ETag no sustituye hash.
4. Antes de confirmar, comprobar tamaño, hash y cabecera FMP1; objeto truncado no se publica.
5. Actualizar versión y contadores en transacción, convertir reserva a uso y liberar tamaño
   anterior. Solo después eliminar objeto anterior. Conflicto conserva respaldo anterior.
6. Completar otra vez devuelve mismo resultado sin cobrar de nuevo. Cancelar después de
   completar no revierte commit. Expiraciones liberan reserva; job limpia huérfanos.
7. Eliminar respaldo libera cuota una sola vez, invalida tickets nuevos y elimina objeto.

Cliente mantiene archivo local si falla reserva, PUT, confirmación, restauración o borrado.
Restauración es explícita y puede reemplazar copia local correspondiente; no hay sincronización
automática ni estrategia silenciosa de last-write-wins.

### Formato binario portable del respaldo y proyecto protegido

`libraryKey` es 32 bytes aleatorios. SHA-512(key) produce 64 bytes: primeros 32 clave AES,
últimos 32 clave HMAC. Sobre JSON UTF-8 de LibraryBundle:

```text
ASCII FMP1 (4 bytes) || IV aleatorio (16) || AES-256-CBC-PKCS7(JSON) || HMAC-SHA256(todo anterior) (32)
```

Comprobar HMAC en tiempo constante antes de descifrar. No reutilizar IV. No guardar claves
junto a archivos. Bundle descifrado:

```json
{"entry":{"id":"<UUID local N>","title":"Caja","tags":[],"updatedAt":"...",
 "remoteId":null,"version":0,"acquisitionId":null,"designId":null},
 "project":"<base64 de proyecto .lps>","thumbnail":"<PNG base64 240x160>"}
```

Proyecto propio conserva gzip/XML v4 existente. Proyecto con adquisiciones usa mismo sobre
FMP1, conteniendo gzip/XML con comprobantes por trayectoria. No cambiar formato de archivos
propios antiguos. El servidor no necesita descifrar respaldos. Guardar claves del lado
servidor permite restauración entre computadoras tras login.

## 5. Publicaciones gratuitas

| Método y ruta | Cuerpo / respuesta |
|---|---|
| GET `/designs?q=&category=&designerId=` | Página de Design publicados |
| GET `/designs/:id` | Design sin SVG ni URL de contenido |
| POST `/designs` | Publicación del ejemplo inferior → Design |
| PATCH `/designs/:id` | `{"version":1,"title":"Nuevo","description":"...","category":"Cajas","tags":[]}` → Design |
| DELETE `/designs/:id` | `{"version":2}` → 204, retirar catálogo |

```json
{"title":"Caja encastrable","description":"Diseño vectorial de caja",
 "category":"Cajas","tags":["caja","encastre"],
 "svg":"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20mm\" height=\"20mm\" viewBox=\"0 0 20 20\"><path fill=\"none\" stroke=\"#000000\" d=\"M0 0 L20 0 L20 20 Z\"/></svg>",
 "licenseVersion":"physical-products-v1","ownershipConfirmed":true,"price":0}
```

Design:

```json
{"id":"des_01","title":"Caja encastrable","description":"...","category":"Cajas",
 "tags":["caja"],"designer":{"id":"usr_01","displayName":"Ana","bio":"...","avatarUrl":null},
 "licenseVersion":"physical-products-v1","status":"published","version":1,
 "sha256":"<SHA-256 del SVG canónico UTF-8>","thumbnail":"<PNG base64>"}
```

Publicar exige correo verificado, declaración de autoría y licencia soportada. SVG canónico
es resultado de validar/normalizar; inmutable después de publicar. PATCH solo cambia metadatos,
no geometría/licencia. Cambiar geometría requiere nueva publicación. Evitar copias idénticas
del mismo autor con índice de hash. Backend no considera original un archivo solo por su hash.

Validación obligatoria antes de almacenar o renderizar: máximo 10 000 000 bytes UTF-8,
DTD y entidades desactivados, namespace SVG, profundidad <=64, <=50 000 elementos,
<=1 000 000 puntos tras aplanado y coordenadas finitas dentro de ±1 000 000 mm.
Elementos permitidos v1: svg, g, path, rect, circle, ellipse, line, polyline, polygon, title,
desc. No scripts, foreignObject, animación, imágenes, texto/fuentes, CSS/style, enlaces,
use/href, recursos externos, url(), atributos on*. Normalizar colores y geometría.
No recuperar URLs ni ejecutar navegador con acceso de red para renderizar.
Miniatura PNG <=256 KB, <=1024x1024, generada en proceso aislado con límites de tiempo/memoria.
Publicar solo después de validar/renderizar y confirmar objetos. Fallos limpian huérfanos.

Publicación inmediata, sin cola de aprobación manual. Rate limit inicial configurable:
20 publicaciones/día por cuenta; 1000 publicaciones activas por cuenta, independientes
de cuota privada. Operador puede ajustar estos límites por abuso/coste.

## 6. Adquisiciones y comprobantes firmados

| Método y ruta | Cuerpo / respuesta |
|---|---|
| POST `/acquisitions` | `{"designId":"des_01","licenseVersion":"physical-products-v1","accepted":true}` → Content |
| GET `/acquisitions` | Página de Content sin svg/receipt, incluyendo id/designId/title |
| POST `/acquisitions/:id/content` | Content completo para cuenta propietaria |

```json
{"id":"acq_01","designId":"des_01","title":"Caja encastrable",
 "svg":"<SVG canónico validado>",
 "receipt":{"payload":"<base64 de JSON UTF-8 exacto>","signature":"<base64 firma RSA>"}}
```

JSON exacto firmado, sin saltos añadidos ni serialización posterior:

```json
{"accountId":"usr_02","designId":"des_01","acquisitionId":"acq_01",
 "sha256":"<SHA-256 hex minúsculas de SVG UTF-8>","licenseVersion":"physical-products-v1"}
```

Firmar payload binario con RSA-SHA256 PKCS#1 v1.5, clave >=2048 bits. Clave privada solo en
servidor/gestor de secretos. Operador configura clave pública en app como XML:
`<RSAKeyValue><Modulus>BASE64</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>`.
No confiar en clave enviada junto a contenido. V1 usa una clave fijada; rotación requiere
actualización coordinada y reemisión de comprobantes, no reemplazo silencioso.

Cliente verifica firma, cuenta, IDs, licencia y hash antes de importar; guarda comprobante
en trayectorias y contenido cifrado. Nunca escribir SVG adquirido en temporal de texto plano.
La adquisición es única por cuenta+diseño. Otra llamada devuelve mismo id/comprobante.
Retirar publicación no elimina adquisiciones; bloqueo administrativo impide nuevas entregas.
Copias offline ya autorizadas no pueden revocarse instantáneamente; documentar ese límite.

Abrir contenido importado no activa salida de máquina automáticamente. Alterar/copy/paste
geometría conserva procedencia. Guardar proyecto adquirido requiere sobre cifrado; bloquear
exportación SVG, G-code y proyecto abierto, incluyendo G-code compilado anteriormente.

## 7. Reportes y administración

POST `/reports` con `{"designId":"des_01","reason":"Posible copia de mi diseño"}` → 204.
Razón 10..2000 caracteres. Registrar usuario, publicación, fecha y requestId; no publicar denuncias.

Operaciones del backend, sin pantalla administrativa incluida en cliente:

| Método y ruta | Requisito / resultado |
|---|---|
| GET `/admin/reports?status=open` | Rol moderator; página de reportes |
| POST `/admin/designs/:id/block` | Rol moderator, `{"reason":"..."}`; 204, ocultar y bloquear entregas futuras |
| POST `/admin/designs/:id/unblock` | Rol moderator, `{"reason":"..."}`; 204 |
| POST `/admin/reports/:id/resolve` | Rol moderator, `{"resolution":"..."}`; 204 |
| POST `/admin/users/:id/suspend` | Rol admin, `{"reason":"..."}`; 204, revocar sesiones |

Todas auditadas; retirar un diseño no borra prueba de licencia/adquisición. Moderación no
ejecuta comandos de máquina ni intenta borrar archivos del cliente.

## 8. Persistencia, operación y aceptación backend

Entidades mínimas: users, sessions, emailTokens, privateLibrary, uploadReservations,
designs, acquisitions, reports, idempotencyRecords y auditEvents. Índices únicos para correo
normalizado, cuenta+diseño adquirido e idempotencia. Transacciones para cuota y versiones.
Backups de DB, claves cifradas y objetos; probar restauración, no solo creación de backup.
Redactar passwords, tokens, libraryKey, URLs firmadas y contenido SVG de logs.

Pruebas requeridas al implementar ExpressJS:

- Cuenta A no lista, descarga ni modifica recursos de B; endpoints públicos no revelan email.
- Dos reservas concurrentes no exceden 200 MB; cancelación/expiración/completar repetido
  no duplican contadores. Hash incorrecto o PUT truncado conserva versión previa.
- Refresh rota tokens y no cambia libraryKey; recuperar contraseña no destruye respaldos.
- Adquirir repetidamente da un único derecho; hash/firma/owner incorrectos se rechazan.
- SVG con XXE, scripts, href, CSS o complejidad excesiva no se almacena ni renderiza.
- Retirar conserva adquisiciones; bloquear deniega nuevas entregas; límites de offline explícitos.
- SDK S3 probado contra Backblaze real para PUT/GET firmados, HEAD, hash y limpieza.
- Pago no cero rechazado. Regla 5:1 documentada pero no bloquea catálogo gratuito v1.

El cliente incluye pruebas con transporte simulado. Esas pruebas **no acreditan** servidor,
correo ni Backblaze desplegados. Ejecutar aceptación real antes de habilitar comunidad a usuarios.
