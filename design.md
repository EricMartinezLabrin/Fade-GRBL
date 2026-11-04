# FadeGRBL — Sistema de diseño

Guía visual y de interacción para modernizar FadeGRBL, aplicación de escritorio basada en LaserGRBL. La interfaz debe sentirse precisa, tranquila y fácil de usar: el dibujo es protagonista; las herramientas acompañan.

Este documento define el diseño objetivo. Su creación no implica que estos estilos ya estén implementados en los formularios.

## 1. Identidad y dirección visual

La referencia es el logo **FADE TECH**, proporcionado en `C:\Users\USER\Pictures\Logos\Logo Cuadrado.png`. Se analizaron los píxeles opacos del archivo: los colores planos principales son blanco `#FFFFFF`, grafito `#393E46` y ámbar `#F6B42A`. Las pequeñas variaciones de los bordes corresponden al suavizado de la imagen; no deben convertirse en nuevos colores de interfaz.

**Dirección:** escritorio técnico moderno, minimalista y funcional. Tema claro por defecto, superficies neutras, jerarquía tipográfica clara y acentos ámbar puntuales. El tema oscuro es una alternativa del mismo sistema.

- Usar espacio, alineación y contraste para organizar; evitar encerrar cada dato en una caja.
- Una sola acción principal por zona. Herramientas frecuentes a la vista; ajustes avanzados bajo una acción identificable.
- Evitar degradados, biseles, texturas, brillos, sombras grandes y paneles decorativos.
- Mantener densidad útil de una herramienta CAD. Minimalismo no significa ocultar funciones ni ampliar controles hasta quitar espacio al canvas.
- Usar español claro, con acentos correctos: «Lienzo», «Capas», «Objetos», «Pasadas», «Potencia», «Velocidad».
- La marca identifica la aplicación; los colores de las capas identifican el trabajo. No intercambiar esos significados.

### Uso del logo

- Conservar proporciones, colores y geometría. No estirar ni reconstruir sus letras con una fuente.
- Utilizarlo una vez en el encabezado o en «Acerca de»; evitar marcas de agua sobre el dibujo.
- El archivo cuadrado contiene mucho margen blanco. Preparar un recurso horizontal recortando únicamente ese margen para el encabezado, sin deformar la marca.
- Reservar 8 unidades de espacio alrededor del recurso. El contenido visible del logo en el encabezado debe medir aproximadamente 24–28 unidades de alto.
- Omitir el lema en tamaños donde deje de ser legible. No reducir todo el archivo cuadrado a un icono diminuto.
- Sobre fondos oscuros, usar una placa blanca discreta mientras no exista una variante de marca adecuada. No invertir automáticamente los colores del original.

## 2. Colores y tokens

Los colores del logo son la base. Los neutros adicionales y los colores de estado son decisiones de interfaz, no colores extraídos de la marca.

### Tema claro — predeterminado

| Token | Valor | Uso |
| --- | --- | --- |
| `BrandAmber` | `#F6B42A` | Acción principal y acentos de marca |
| `BrandGraphite` | `#393E46` | Texto principal e iconos |
| `AppBackground` | `#F6F7F9` | Fondo general |
| `Surface` | `#FFFFFF` | Paneles, diálogos y campos |
| `SurfaceSubtle` | `#F0F2F5` | Encabezados de tabla y agrupaciones suaves |
| `TextPrimary` | `#393E46` | Contenido principal |
| `TextSecondary` | `#626A76` | Ayuda, unidades y datos secundarios |
| `BorderSubtle` | `#D6DAE1` | Separadores decorativos |
| `BorderControl` | `#7C8491` | Contorno de campos y controles sin otra delimitación |
| `SelectionBackground` | `#FFF3D6` | Filas o herramientas seleccionadas |
| `SelectionForeground` | `#393E46` | Texto sobre selección |
| `FocusRing` | `#8A5A00` | Foco de teclado |
| `PrimaryHover` | `#E5A31B` | Botón principal bajo el cursor |
| `PrimaryPressed` | `#D49412` | Botón principal presionado |
| `DisabledBackground` | `#E8EBEF` | Superficie deshabilitada |
| `DisabledForeground` | `#626A76` | Texto deshabilitado |
| `Success` | `#18794E` | Trabajo terminado o confirmación válida |
| `Danger` | `#B42318` | Detener, alarma o error |
| `Warning` | `#8A5A00` | Advertencia con icono y texto |
| `Information` | `#175CD3` | Información y enlaces |

### Tema oscuro — alternativa

| Token | Valor |
| --- | --- |
| `AppBackground` | `#20242B` |
| `Surface` | `#292E36` |
| `SurfaceSubtle` | `#323842` |
| `TextPrimary` | `#F6F7F9` |
| `TextSecondary` | `#BCC3CD` |
| `BorderSubtle` | `#515B69` |
| `BorderControl` | `#7C8491` |
| `SelectionBackground` | `#463B24` |
| `SelectionForeground` | `#F6F7F9` |
| `FocusRing` | `#F6B42A` |
| `DisabledBackground` | `#323842` |
| `DisabledForeground` | `#BCC3CD` |
| `Success` | `#6CE9A6` |
| `Danger` | `#F97066` |
| `Warning` | `#F6B42A` |
| `Information` | `#84ADFF` |

`BrandAmber`, `BrandGraphite`, `PrimaryHover` y `PrimaryPressed` conservan sus valores. El texto de los botones ámbar siempre utiliza `BrandGraphite`, también en tema oscuro.

### Contraste y significado

- Objetivo mínimo: **4.5:1** para texto normal y **3:1** para límites de controles, indicadores y foco que sean necesarios para reconocer o usar un elemento.
- Pares calculados: grafito sobre ámbar **5.88:1**; grafito sobre blanco **10.76:1**; texto secundario sobre blanco **5.47:1**; `BorderControl` sobre blanco **3.77:1**.
- No usar texto blanco sobre el ámbar de marca: su contraste es solamente **1.83:1**.
- `BorderSubtle` sirve para separar superficies; no debe ser el único límite de un campo editable.
- El ámbar de marca no significa por sí solo «advertencia». Advertencias requieren símbolo y mensaje.
- Cada estado debe distinguirse mediante texto, icono o forma, además del color.

## 3. Tipografía, espaciado y forma

- Fuente de interfaz: **Segoe UI**, con la fuente del sistema como alternativa. No añadir una dependencia de descarga de fuentes.
- Texto de controles: 10 pt regular. Etiquetas y ayudas: 9 pt. Encabezados de panel: 11 pt semibold o bold. Títulos de diálogo: 14–16 pt.
- Consola y G-code: **Consolas**, 9–10 pt; alternativa monospace del sistema.
- Alinear números a la derecha en tablas. Mantener unidades visibles en el encabezado o junto al campo: `mm`, `mm/min`, `%`.
- Mostrar tiempos con ancho estable: `00:12:34`. No cambiar el tamaño del contenedor cada segundo.
- Escala espacial: **4, 8, 12, 16, 24 y 32** unidades lógicas a 96 DPI. Padding general de panel: 16; separación entre controles relacionados: 8; entre grupos: 16.
- Botones y campos: altura mínima 32; botones principales: 36. Filas de tabla: aproximadamente 30. Iconos: 16 o 20; área interactiva mínima 32 × 32.
- Bordes de 1 unidad; indicador de foco de 2. Radio de esquina de 4–6 cuando el control lo admita sin comprometer dibujo ni rendimiento. Los controles nativos rectangulares son aceptables.
- No usar sombras para separar todos los paneles. Reservar una sombra ligera para superficies flotantes si la plataforma la ofrece.
- Toda dimensión debe escalar con DPI; no interpretar estas medidas como píxeles físicos fijos.

## 4. Estructura de la ventana

Orden visual de izquierda a derecha: **conexión y máquina → lienzo → capas y propiedades**. La franja inferior reúne progreso y estado. Conservar el comportamiento real de los paneles existentes al aplicar esta organización.

### Encabezado y herramientas

- Encabezado compacto: marca, nombre del proyecto y estado de conexión. Evitar un banner de presentación.
- Menú convencional para archivo, edición, vista y ayuda. Barra de herramientas debajo con acciones agrupadas y separadores discretos.
- Mantener «Abrir», «Guardar» e «Iniciar» identificables. En ejecución, dar prioridad a «Pausar», «Reanudar» y «Detener».
- Herramientas del dibujo en un solo nivel: selección de capas/objetos, copiar, pegar, duplicar y dimensiones. Desbordamiento accesible cuando falte ancho.

### Lienzo

- Debe ocupar la mayor superficie disponible, sin tarjetas ni mensajes permanentes superpuestos.
- Tema claro: fondo blanco, cuadrícula menor `#EDF0F4`, mayor `#D6DAE1` y reglas con `TextSecondary`.
- Tema oscuro: fondo `#181C22`, cuadrícula menor `#242B34`, mayor `#303741` y reglas con `TextSecondary`.
- Cuadrícula secundaria tenue y opcional; límites de trabajo claramente identificables. Los textos de medidas deben seguir siendo legibles.
- Conservar el color real de cada capa. La selección añade contorno ámbar con base contrastante, patrón y tiradores; no reemplaza el color de corte.
- Diferenciar movimiento de traslado y corte mediante trazo discontinuo/continuo, además de tono. No confundir trayectoria enviada con corte físicamente terminado.
- Tiradores de tamaño de al menos 10 unidades, con zona de interacción mayor que el dibujo del tirador.
- Ancho/alto y bloqueo de proporción permanecen disponibles junto al lienzo. Arrastrar y escribir medidas deben producir resultados coherentes.

### Capas y propiedades

- Panel derecho redimensionable; ancho inicial orientativo de 430, mínimo expandido de 280. Permitir plegarlo, conservando una acción clara para abrirlo.
- Tabla plana: encabezado suave, sin cuadrícula vertical pesada ni bordes repetidos. Resaltar fila seleccionada con `SelectionBackground` y un indicador adicional.
- Muestra de color separada de texto, foco y selección. «Visible» y «Salida» son controles distintos, con tooltip y estado inequívoco.
- Prioridad de columnas: salida, visibilidad, nombre/color, modo, velocidad, potencia y pasadas. Propiedades menos frecuentes en «Avanzado».
- Conservar selección múltiple, `Ctrl+A`, copiar/pegar/duplicar y orden de capas. No perder selección ni posición de desplazamiento al actualizar datos.
- Durante edición de una celda, conservar el texto y validar al confirmar o salir. Un error se muestra junto al campo; no reconstruir la tabla mientras se escribe.

### Progreso y control de máquina

- Estado visible y estable: «Listo», «Cortando», «Pausado», «Reanudando», «Terminado» o «Interrumpido».
- Mostrar porcentaje total, capa/objeto actual, pasada actual/total y tiempo restante estimado. Jerarquía: estado y porcentaje primero; detalle después.
- Diferenciar claramente «Enviado» de «Avance estimado/confirmado». La recepción de `ok` no demuestra que el láser terminó de moverse.
- Reservar altura para el bloque de información durante un trabajo. Construir cada texto completo antes de asignarlo y actualizar solo valores que cambien.
- Conservar doble búfer y agrupación de cambios de layout. No alternar visibilidad ni reconstruir controles en cada tick.
- «Detener» siempre visible durante la ejecución y separado de «Reanudar». Usar etiqueta e icono, además de color rojo. No presentarlo como sustituto de un paro físico de emergencia.
- La consola es secundaria y plegable; una alarma activa sigue visible aunque se cierre la consola.

### Adaptación a espacio reducido

- Usar divisores redimensionables y recordar tamaños dentro del área visible de la pantalla.
- Al reducir ancho, plegar primero consola y propiedades secundarias; permitir desplazamiento de la tabla y desbordamiento de herramientas.
- Nunca ocultar el estado de conexión ni los controles de pausa y detención por falta de espacio.
- Ajustar tamaño inicial al área de trabajo del monitor. Evitar mínimos rígidos que dejen botones fuera de pantalla a DPI alto.

## 5. Componentes e interacción

| Componente | Regla |
| --- | --- |
| Botón principal | Fondo ámbar, texto grafito, etiqueta concreta. Solo una acción dominante por sección. |
| Botón secundario | Superficie neutra, texto principal y borde discreto; sin competir con el principal. |
| Acción peligrosa | Texto/icono de peligro; «Detener» puede usar fondo de peligro con texto contrastante. |
| Botón de icono | Icono coherente, tooltip con nombre y atajo, foco visible y área interactiva suficiente. |
| Campo editable | Fondo de superficie, límite reconocible y etiqueta persistente. No depender de placeholder. |
| Campo inválido | Borde de peligro, icono y explicación breve; conservar valor escrito. |
| Selección de modo | Radio buttons o selector explícito con etiqueta; estado seleccionado visible sin depender del color. |
| Control deshabilitado | Texto legible, aspecto atenuado y motivo disponible cuando ayude a continuar. |
| Diálogo | Título concreto, explicación breve, contenido alineado y acciones consistentes al pie. |
| Estado vacío | Instrucción útil: «Abre un SVG o una imagen para comenzar», con acción «Abrir archivo». |

- Usar una sola familia de iconos, preferentemente la ya integrada mediante `IconsMgr`; evitar mezclar emoji, iconos de distinto grosor y dibujos decorativos.
- Texto visible para acciones críticas. Los atajos aparecen en tooltip o menú: `Ctrl+C`, `Ctrl+V`, `Ctrl+D` y `Ctrl+A`.
- Los campos de texto conservan sus atajos normales; copiar texto no debe duplicar objetos ni activar la máquina.
- Recorrido de tabulación coherente, foco visible, etiquetas accesibles y operación por teclado.
- Hover y presión cambian tono; no desplazan ni redimensionan controles. Evitar animaciones continuas, parpadeo y transiciones que retrasen una acción de máquina.

### Elección de pasadas

Antes de iniciar un trabajo nuevo, presentar las dos opciones con descripción clara:

- **Por objeto — interiores primero:** completa todas las vueltas del objeto antes del siguiente, incluso entre capas distintas. Total por objeto = pasadas de su capa × contador de repeticiones.
- **Por capa — comportamiento actual:** realiza las pasadas de la capa según el flujo existente y conserva las repeticiones del trabajo completo.

Usar un ejemplo breve cuando sea útil: «Capa: 1 pasada · Repeticiones: 10 → 10 vueltas por objeto». El orden de los interiores debe basarse en contención geométrica; el estilo visual no debe modificarlo. Reanudar conserva el modo del trabajo en curso.

## 6. Aplicación en el código existente

La implementación debe respetar **WinForms y .NET Framework 4.0**, así como las restricciones del compilador del proyecto. No requiere migrar a otro framework ni introducir una biblioteca visual pesada.

- Centralizar los tokens en el sistema de temas de `LaserGRBL/ColorScheme.cs`. Adaptar `IColorScheme` y sus implementaciones de forma coherente si hacen falta roles nuevos; evitar valores hexadecimales dispersos por formularios.
- Mapear `FormBackColor` a superficies de formulario, `FormForeColor` a texto, los roles `Preview*` al lienzo y `ControlsBorder` a límites de controles. Separar borde decorativo y borde interactivo si el sistema actual solo ofrece un rol.
- Definir selección, foco y estados mediante roles explícitos; no obtener todos los tonos aclarando u oscureciendo un fondo con un porcentaje arbitrario.
- Aplicar la misma paleta a menús, `ToolStrip`, `DataGridView`, campos, tooltips y diálogos. Mantener compatibilidad con los temas existentes y no reemplazar colores de documentos.
- En `LaserGRBL/PreviewForm.cs`, conservar divisores, modos de selección, edición numérica, selección múltiple y herramientas del lienzo al modernizar la composición.
- En `LaserGRBL/ConnectLogForm.JobExecution.cs`, preservar las actualizaciones estables del progreso y los estados de control del trabajo.
- Mantener layout de controles en sus `.Designer.cs` cuando corresponda y comportamiento en los archivos `.cs`. Usar `TableLayoutPanel`, `FlowLayoutPanel` y `SplitContainer` para adaptación real.
- Reutilizar recursos e iconos del proyecto. Preparar el logo como recurso de la aplicación cuando se implemente el encabezado; no depender de la ruta personal del archivo original en tiempo de ejecución.
- Incorporar textos visibles al `.resx` neutro y al español. Mantener claves existentes, formato de números por cultura y texto Unicode correcto.
- Preferir controles nativos bien configurados. Limitar dibujo personalizado a necesidades concretas y conservar su accesibilidad, foco y doble búfer.
- No cambiar comunicación, permisos de edición, generación de G-code, potencia, velocidad, coordenadas, pasadas ni recuperación como efecto secundario de cambios de estilo.

## 7. Criterios de aceptación visual

- [ ] La paleta coincide con el logo y el ámbar aparece como acento, no como fondo dominante.
- [ ] Canvas amplio, herramientas agrupadas y una jerarquía clara sin paneles decorativos redundantes.
- [ ] Botones ámbar con texto grafito; foco, campos y estados mantienen contraste verificable.
- [ ] Temas claro y oscuro sin textos invisibles, iconos perdidos ni fondos de controles ajenos al tema.
- [ ] Selección distinguible sobre capas ámbar, blancas y negras, sin modificar sus colores reales.
- [ ] «Visible» y «Salida» claramente diferenciados; copias mantienen salida desactivada según el flujo actual.
- [ ] Se pueden redimensionar paneles, editar medidas, seleccionar varios elementos y operar con teclado.
- [ ] Texto español sin caracteres corruptos; etiquetas, unidades y cifras completas.
- [ ] Sin parpadeo ni saltos repetidos de tamaño durante progreso, pausa y reanudación.
- [ ] Detener, pausar y estado de conexión accesibles con la consola y propiedades plegadas.
- [ ] Diálogos completos y utilizables en 1024 × 768, 1366 × 768 y 1920 × 1080, comprobando escalas de 100 %, 125 %, 150 % y 200 % y adaptación al área disponible.
- [ ] Compilación Release y pruebas pertinentes al cambiar controles o comportamiento; capturas para revisar cambios visuales. Una compilación correcta no sustituye la revisión visual ni una prueba física de corte.

**Regla de mantenimiento:** toda pantalla nueva debe reutilizar estos roles, medidas, componentes y criterios antes de introducir un patrón visual adicional.

## 8. Implementación compartida y revisión

Los temas «Fade · Claro» y «Fade · Oscuro» se seleccionan en **Colores**; el claro es el valor inicial del nuevo sistema. La preferencia se guarda en `Fade Design Theme`. Los esquemas anteriores siguen disponibles.

`FadeColorScheme` define la paleta y `FadeTheme` aplica componentes, menús, tablas, foco y estados a la ventana principal y a los formularios de la aplicación. Los diálogos nativos del sistema conservan su presentación de Windows. El encabezado usa el logo incorporado como recurso, sin depender de la carpeta personal del usuario.

Para revisar la ventana y los diálogos con el runtime real de la aplicación:

```powershell
msbuild LaserGRBL.sln /p:Configuration=Release
powershell.exe -NoProfile -File scripts\Render-FadeDesign.ps1
```

El harness utiliza GDI, un documento de muestra y una ventana que omite el arranque normal: no conecta dispositivos ni inicia trabajos. Genera capturas claras y oscuras en `LaserGRBL.Tests/TestResults/design/`, en 1024 × 768, 1280 × 800 y 1920 × 1080, junto con los diálogos de color, pasadas, parámetros y reanudación. Requiere Windows PowerShell con .NET Framework; el renderer GDI existente utiliza `Thread.Abort`, no disponible en el runtime moderno del proyecto de pruebas. Las capturas complementan las pruebas y no sustituyen revisar DPI alto, interacción en una sesión normal o un corte físico.
