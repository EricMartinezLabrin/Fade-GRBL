# Uso y configuración de Comunidad FadeGRBL

Esta versión implementa cliente de escritorio. Primero implementar API descrita en
[marketplace-server-contract.md](marketplace-server-contract.md); no incluye backend ExpressJS.

1. Abrir menú **Comunidad → Mi biblioteca**. En configuración escribir origen HTTPS del
   servidor (sin `/api/v1`) y clave pública RSA XML entregada por administrador.
2. Crear cuenta con correo y contraseña; iniciar sesión. En **Mi perfil**, verificar correo
   con código recibido. Correo verificado es obligatorio para publicar.
3. **Guardar proyecto en biblioteca** conserva capas, texto y parámetros localmente.
   **Importar SVG propio** admite subconjunto vectorial seguro descrito en contrato.
   **Abrir en lienzo** reemplaza documento guardado; **Insertar en lienzo** añade diseño
   al proyecto actual para componer trabajos, conservando procedencia y restricciones.
4. Seleccionar entrada local y **Respaldar en nube**. **Consultar nube** muestra cuota y
   respaldos. **Restaurar respaldo** reemplaza copia local correspondiente tras confirmación.
   **Eliminar respaldo remoto** conserva copia local. No hay sincronización automática.
5. En **Marketplace**, buscar diseños o diseñadores. **Ver catálogo del autor** filtra por
   diseñador; **Más resultados** continúa búsqueda. Detalle muestra licencia y miniatura.
6. **Añadir a mi biblioteca** pide aceptar licencia; agrega adquisición sin descargar SVG
   abierto. **Restaurar mis adquisiciones** recupera catálogo personal en otra computadora.
7. **Publicar diseño propio** utiliza documento actual; si contiene raster, seleccionar
   capas u objetos vectoriales antes. Texto propio se convierte a contornos. Confirmar autoría.
   Publicaciones propias permiten editar metadatos o retirarlas.

Biblioteca local requiere login inicial y sesión guardada; después funciona offline.
Archivos propios y control de máquina siempre funcionan sin cuenta ni servidor configurado.
No cambiar servidor/cuenta durante trabajo activo. Ventana Comunidad es independiente y
operaciones HTTP corren fuera del hilo de UI; controles de máquina siguen en ventana principal.

## Archivos y límites

Datos en `%APPDATA%\LaserGRBL\Community`. `session.bin` protegido con DPAPI CurrentUser.
Bibliotecas están separadas por hash de origen+ID de usuario; bundles `.fbl` cifrados con clave
de cuenta. Respaldo nube conserva formato portable entre equipos de esa cuenta.
No compartir session.bin ni guardar contraseñas/credenciales Backblaze en configuración.

Proyectos `.lps` propios conservan formato existente. Proyectos con diseños adquiridos tienen
cabecera FMP1 cifrada y necesitan sesión de cuenta correspondiente y clave pública configurada.
Otras versiones de FadeGRBL no podrán abrirlos. Proyecto protegido puede editarse, guardarse
y enviarse a máquina; no exportarse a SVG/G-code. Restricción acompaña objetos copiados.

200 MB significa 200 000 000 bytes. Cada entrada cifrada de biblioteca admite hasta 100 MB;
SVG publicados/importados admiten hasta 10 MB. Adquisiciones no consumen cuota; un proyecto personal
compuesto y respaldado sí. Actualizar respaldo reserva temporalmente tamaño de versión nueva
además de anterior. Si no cabe, eliminar respaldos que ya no se necesiten o conservar local.
No existe todavía compra de espacio ni publicación de pago.

Cerrar sesión ofrece guardar proyecto protegido y cierra acceso. Conservar sesiones offline
permite uso sin internet; revocación remota no puede borrar copias locales instantáneamente.
Protección dentro de app no impide ingeniería inversa ni extracción por administrador del equipo.

## Verificación

Compilar Release con Visual Studio MSBuild. Ejecutar pruebas Community y Layering con
DOTNET_ROLL_FORWARD=Major y BuildProjectReferences=false. Script
`scripts/Test-CommunityUI.ps1` genera capturas de formularios reales con datos ficticios,
sin red, cuentas reales ni dispositivos. No acredita interacción normal, DPI alto ni corte físico.
Después de desplegar backend, probar correo, dos cuentas, cuota, archivos reales Backblaze,
adquisición, restauración en segundo equipo y respuestas de error antes de publicar versión.
