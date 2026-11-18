# Validación con el mouse físico

Usa el AM Infinity .97 por 2,4 GHz, con el intervalo de monitoreo de 60 s. Mantén el mismo firmware, perfil, polling rate, iluminación y ajuste de suspensión en todas las pasadas. Cierra AM Master y el configurador web durante las pasadas A y B. Anota la versión de Windows y del firmware.

## A. Referencia sin el monitor

1. Sal del monitor desde la bandeja. Mueve el .97 durante unos segundos. Para medir el reposo, empieza a cronometrar en cuanto lo sueltes.
2. Déjalo inmóvil durante cinco minutos. Registra cuándo aparece la señal de reposo y si cambia durante la espera. Si dispones de una medición directa del consumo **del mouse**, registra la corriente en actividad y en reposo. Un medidor USB conectado al receptor solo mide el consumo del receptor. La iluminación y el porcentaje del mouse sirven como observaciones auxiliares.
3. Mueve el .97 una vez y registra si la primera entrada llega normalmente. Repite tres veces para obtener una referencia de la variación normal.

## B. Con el monitor

1. Desde la carpeta del proyecto, ejecuta **.\dist\win-x64\AMInfinity97Tray.exe --diagnostico** en PowerShell. Si el monitor ya estaba abierto, sal de la instancia anterior primero. Mueve el .97 y revisa que aparezcan ambos niveles, sus horas y el estado de carga.
2. Actívalo durante al menos dos minutos. Comprueba que se obtienen lecturas automáticas separadas por un minuto o más. Repite con entrada relativa dentro de un juego si ese es tu caso de uso.
3. Detén el mouse y repite la espera de cinco minutos. Anota la señal de reposo, la corriente si está disponible y cualquier reactivación espontánea. Escribe con el teclado. Si tienes otro puntero, úsalo para abrir el panel y pulsar **Actualizar ahora**. En ambos casos, el monitor debe conservar el nivel anterior y esperar al .97. Las consultas que empezaron justo antes de soltarlo pueden terminar durante los primeros 2,5 s.
4. Mueve el .97 de nuevo. Debe responder normalmente. Una solicitud manual pendiente se ejecuta después de la estabilización si han pasado 5 segundos desde el último intento. Las rondas automáticas siguen el intervalo configurado. Si el monitor estaba en espera por acceso denegado, espera a que termine el aplazamiento y vuelve a mover el .97.
5. Repite la pasada tres veces. Exporta el registro desde **Guardar diagnóstico…** y comprueba que durante el reposo no aparecen eventos **Consulta Mouse** ni **Consulta Spare**. Compara los tiempos y corrientes con la variación observada en A. Si hay un indicio de vigilia adicional, conserva el registro y repite con el monitor cerrado.

Una comparación de cinco minutos permite verificar la pausa de la aplicación y detectar diferencias visibles. Para validar consumo, compara trazas de corriente del propio mouse con la misma instrumentación en A y B, o haz pasadas de varias horas con las mismas condiciones y un patrón de uso equivalente. Una traza de USB del receptor verifica los informes enviados, pero por sí sola no mide la radio ni distingue suspensión de apagado.

## C. Recuperación y coexistencia

| Acción | Resultado esperado |
| --- | --- |
| Retirar el repuesto y volver a insertarlo | En la siguiente ronda con actividad debe aparecer «No instalada» y luego volver el porcentaje. Si el repuesto está en carga, debe indicarlo. |
| Apagar el mouse o cambiar su batería | El último porcentaje se conserva ante falta de respuesta. Si se devuelve una respuesta válida de 0%, se muestra 0%. |
| Retirar y reconectar el receptor | Aparece el estado desconectado y se conservan los valores anteriores. La primera lectura siguiente requiere actividad física y respetar el intervalo. |
| Suspender y reanudar Windows | La aplicación sigue en la bandeja. Se necesita actividad nueva del .97 para consultar. |
| Abrir AM Master mientras se usa el monitor | Abre la pantalla de configuración o lee los ajustes existentes. Si hay acceso denegado, el monitor conserva sus lecturas y aplaza los intentos. Si hay interferencia, pausa el monitor y comprueba que AM Master recupera el acceso. |
| Usar el monitor pausado para configurar en AM Master | El diagnóstico queda sin consultas. Después de cerrar AM Master, reanuda el monitor y vuelve a mover el .97. |
| Dejar la aplicación una hora en reposo | Anota el tiempo de CPU acumulado y la memoria en Administrador de tareas antes y después. El tiempo de CPU debería variar mínimamente y la memoria estabilizarse. Registra también una medición durante juego a 8 kHz si usas esa frecuencia. |

Para compartir el resultado, adjunta el diagnóstico y los tiempos de entrada en reposo de A y B, el comportamiento al volver a mover el .97, los dos porcentajes que se mostraron y el resultado con AM Master. Incluye corriente o memoria si las mediste.
