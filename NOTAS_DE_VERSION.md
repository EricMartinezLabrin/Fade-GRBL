# FadeGRBL 1.0.0 — Notas de versión

4 de noviembre de 2026. Cambios acumulados desde LaserGRBL 7.14.1 (clon: 1f9337b).

## Capas y edición

- Capas vectoriales y rasterizadas con nombre, color, orden, velocidad, potencia y pasadas independientes; modos de línea y relleno.
- Crear, duplicar, combinar, reordenar y eliminar capas completas, con selección múltiple y confirmación de borrado.
- Selección, movimiento y eliminación de objetos dentro de un SVG; corrección de importación y resolución del DTD.
- Panel de capas plegable y redimensionable; edición validada al terminar de escribir.
- Movimiento y tamaño desde el lienzo o campos en mm, con bloqueo de proporción.
- Copiar, pegar y duplicar capas u objetos. Copias independientes, desplazadas 5 mm y con salida apagada.
- Visibilidad y salida independientes: las capas sin salida quedan como referencia y no conectan con la trayectoria; al activarlas vuelven a conectarse.
- Proyectos de capas en XML comprimido, conservando contenido y ajustes; lectura de proyectos heredados restringida a tipos admitidos.

## Ejecución

- Pasadas por capa o por objeto, con interiores primero y repeticiones combinadas.
- Progreso por capa/objeto y pasada, tiempo estimado conservador y menos parpadeo.
- Pausa, reanudación, detención y ajustes pendientes de velocidad, potencia y pasadas.
- Recuperación y retroceso dentro de la pasada, con comprobaciones de estado y posición; bloqueo de edición durante trabajos activos.

## Identidad y distribución

- Temas Fade claro/oscuro, logo, paleta blanco/grafito/ámbar, textos en español y nueva imagen de inicio FadeGRBL.
- Interfaz y diálogos unificados; guía design.md y capturas de revisión.
- .NET Framework 4.8 e instalador FadeGRBL 1.0.0.

## Validación

Release correcta, 66 pruebas aprobadas y capturas GDI revisadas. Instalador sin regenerar. Pendientes: sesión normal, DPI alto, OpenGL y pruebas físicas de corte y recuperación.