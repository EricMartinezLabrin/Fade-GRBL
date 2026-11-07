# FadeGRBL 1.0.0 — Primera versión

6 de noviembre de 2026. Inicio de numeración propia de FadeGRBL.

## Lienzo y edición

- Capas vectoriales e imágenes con color, nombre, orden, velocidad, potencia, pasadas, visibilidad y salida.
- Deshacer y rehacer hasta 100 operaciones; cortar, copiar, pegar y duplicar capas u objetos.
- Vista editable inmediata, independiente del G-code. Ctrl+Z actualiza dibujo sin pasos invisibles por medidas sin editar.
- Agrupar y desagrupar; reconocimiento de grupos anidados en SVG.
- Fusión automática de capas vectoriales del mismo color, conservando ajustes de destino. Copias vectoriales comparten salida y ajustes; imágenes mantienen capas independientes.
- Paleta al pulsar muestra o código de color, con colores personalizados y cuentagotas.
- Rectángulos, elipses y líneas con extremos editables. Texto directo sobre lienzo, fuentes instaladas y conversión a contornos.
- Alineación respecto a mesa o último elemento seleccionado, que permanece fijo.
- Selector hacia abajo: encierra por completo. Hacia arriba: selecciona elementos tocados. Ctrl añade; Escape cancela.
- Ruedita presionada desplaza; estirar, girar e inclinar con manejadores, campos numéricos, pivote ajustable y bloqueo de proporciones.
- Proyectos v4 con lectura de v2/v3/v4; conserva grupos, texto y transformaciones.

## Ejecución y presentación

- Pasadas por capa u objeto, interiores primero, progreso, pausa, reanudación, ajustes pendientes y recuperación con comprobaciones de estado y posición.
- Edición bloqueada durante trabajos activos.
- Temas claro/oscuro Fade, interfaz en español y paneles redimensionables.
- .NET Framework 4.8; ejecutable e instalador identificados como FadeGRBL 1.0.0.

## Verificación

Release, pruebas de capas y comprobaciones nativas; capturas GDI revisadas. Pendientes: interacción normal OpenGL, DPI alto y pruebas físicas de corte y recuperación.
