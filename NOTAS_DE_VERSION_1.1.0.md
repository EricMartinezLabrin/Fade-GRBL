# FadeGRBL 1.1.0 — Edición directa del lienzo

6 de noviembre de 2026.

## Novedades

- Vista editable inmediata, independiente de la compilación de G-code.
- Texto directamente sobre el lienzo: clic para escribir y doble clic para editar, sin ventana modal.
- Fuentes instaladas, estilos, tamaño en mm, alineación y texto multilínea. Enter añade línea; Ctrl+Enter o clic fuera confirma; Escape cancela.
- Giro, inclinación y estiramiento de objetos, imágenes, capas, grupos y selección completa, mediante manejadores y campos numéricos.
- Pivote ajustable con nueve posiciones; Shift ajusta el giro a intervalos de 15°.
- Líneas con extremos arrastrables, longitud y orientación editables.

## Correcciones

- Deshacer y rehacer actualizan el dibujo sin esperar la generación de trayectorias. Las medidas sin editar no añaden pasos invisibles al historial.
- Cada arrastre o sesión de texto confirmada constituye una operación deshacible; cancelar restaura el estado anterior.
- El texto conserva su transformación al editarlo. Una fuente ausente mantiene los contornos guardados y requiere sustitución explícita.
- Mejora de contraste para geometría oscura sobre fondo oscuro, sin modificar el color de corte.
- Selección con contorno azul/cian para la forma y marco con manejadores ámbar, diferenciados en ambos temas.
- Se descartan compilaciones obsoletas; ejecutar requiere G-code correspondiente a la revisión vigente.

## Compatibilidad y validación

- Proyectos en formato v4; lectura de v2, v3 y v4. El formato del proyecto es independiente de la versión de aplicación.
- Se mantiene .NET Framework 4.8.
- Compilación Release, 151 pruebas de capas y 94 comprobaciones nativas aprobadas. Capturas revisadas en temas claro y oscuro.
- Pendientes: comprobación manual en OpenGL, DPI alto y corte físico.
