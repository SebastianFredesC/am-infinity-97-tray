using System.Drawing;
using System.Text.Json;
using AMInfinity97Tray;

if (args is ["--receptor"])
{
    var discovery = HidBatteryService.Discover();
    Console.WriteLine($"Descubrimiento nativo sin informes HID: {discovery.Status}");
    return discovery.Status is HidStatus.Ready or HidStatus.Missing ? 0 : 1;
}

int count = 0;
Check("Petición exacta del mouse", () =>
    Expect(BatteryProtocol.Request(BatteryTarget.Mouse, 62).AsSpan(0, 9).SequenceEqual(
        new byte[] { 0x14, 6, 0x80, 0x05, 0x5A, 2, 0, 0xCF, 0x30 })));
Check("Petición exacta de repuesto", () =>
{
    var request = BatteryProtocol.Request(BatteryTarget.Spare, 64);
    Expect(request.AsSpan(0, 9).SequenceEqual(new byte[] { 0x14, 6, 0, 5, 0x5A, 2, 0, 0x0F, 0x30 }));
    Expect(request.AsSpan(9).IndexOfAnyExcept((byte)0) < 0);
});
Check("Salida 06 con ruta local para mouse y repuesto", () =>
{
    Expect(BatteryProtocol.Request(BatteryTarget.Mouse, 62, BatteryProtocol.OutputReportId).AsSpan(0, 9)
        .SequenceEqual(new byte[] { 6, 6, 0, 5, 0x5A, 2, 0, 0xCF, 0x30 }));
    Expect(BatteryProtocol.Request(BatteryTarget.Spare, 62, BatteryProtocol.OutputReportId).AsSpan(0, 9)
        .SequenceEqual(new byte[] { 6, 6, 0, 5, 0x5A, 2, 0, 0x0F, 0x30 }));
});
Check("Entrada 07 valida el comando, el formato y el tamaño", () =>
{
    var frame = Frame(0x30CF, [0, 0, 85, 0, 1, 0, 0]);
    var report = Report(frame);
    report[0] = BatteryProtocol.InputReportId;
    Expect(BatteryProtocol.Parse(report, BatteryTarget.Mouse, BatteryProtocol.InputReportId) ==
        new BatteryReply(ReplyStatus.Level, 85));
    Expect(BatteryProtocol.Parse(report, BatteryTarget.Spare, BatteryProtocol.InputReportId).Status == ReplyStatus.Pending);
    Expect(BatteryProtocol.Parse(report.AsSpan(0, 12), BatteryTarget.Mouse, BatteryProtocol.InputReportId).Status == ReplyStatus.Invalid);
    Expect(Mouse(report).Status == ReplyStatus.Invalid);
    frame[0] = 0x15;
    byte[] bare = [BatteryProtocol.InputReportId, .. frame];
    Expect(BatteryProtocol.Parse(bare, BatteryTarget.Mouse, BatteryProtocol.InputReportId).Percent == 85);
});
foreach (byte percent in new byte[] { 0, 1, 5, 10, 20, 30, 31, 85, 100 })
{
    Check($"Porcentaje válido del mouse: {percent}", () =>
        Expect(Mouse(Report(Frame(0x30CF, [0, 0, percent, 0, 1]))) == new BatteryReply(ReplyStatus.Level, percent)));
}
Check("100% con carga todavía activa", () =>
    Expect(Spare(Report(Frame(0x300F, [0, 1, 100, 0, 1]))) == new BatteryReply(ReplyStatus.Level, 100, true)));
Check("100% con carga completa", () =>
    Expect(Spare(Report(Frame(0x300F, [0, 0, 100, 0, 1]))) == new BatteryReply(ReplyStatus.Level, 100)));
Check("Batería ausente distinta de 0%", () =>
    Expect(Spare(Report(Frame(0x300F, [0, 0, 0, 0, 0]))).Status == ReplyStatus.Absent));
Check("Rechaza 101% y 255%", () =>
{
    Expect(Mouse(Report(Frame(0x30CF, [0, 0, 101, 0, 1]))).Status == ReplyStatus.Invalid);
    Expect(Mouse(Report(Frame(0x30CF, [0, 0, 255, 0, 1]))).Status == ReplyStatus.Invalid);
});
Check("Firmware rechazó el comando", () =>
    Expect(Mouse(Report(Frame(0x30CF, [3]))).Status == ReplyStatus.Rejected));
Check("Búfer vacío cuando el receptor está ocupado", () =>
    Expect(Mouse([0x14, 0, 0]).Status == ReplyStatus.Pending));
Check("ID incorrecto y encabezados truncados", () =>
{
    Expect(Mouse([0, 0, 0]).Status == ReplyStatus.Invalid);
    Expect(Mouse([0x14, 0]).Status == ReplyStatus.Invalid);
    Expect(Mouse([0x14, 6, 0, 5]).Status == ReplyStatus.Invalid);
});
Check("Campo de batería incompleto", () =>
    Expect(Mouse(Report(Frame(0x30CF, [0, 0, 85, 0]))).Status == ReplyStatus.Invalid));
Check("Longitud interna falsa no puede utilizar relleno", () =>
{
    var report = Report(Frame(0x30CF, [0, 0, 85, 0]));
    report[5]++;
    Expect(Mouse(report).Status == ReplyStatus.Invalid);
});
Check("Comando inesperado y ACK se ignoran", () =>
{
    Expect(Mouse(Report(Frame(0x300F, [0, 0, 0, 0, 1]))).Status == ReplyStatus.Pending);
    Expect(Mouse(Report(Frame(0x30CF, [0, 0, 0, 0, 1], 0x5D))).Status == ReplyStatus.Pending);
});
Check("Se busca respuesta en un informe con varios mensajes", () =>
{
    byte[] ack = Frame(0x30CF, [0], 0x5D);
    byte[] report = Report(ack, Frame(0x30CF, [0, 0, 85, 0, 1]));
    Expect(Mouse(report).Percent == 85);
    report[1] = (byte)ack.Length;
    Expect(Mouse(report).Percent == 85);
});
Check("Respuestas duplicadas no se aceptan", () =>
{
    var frame = Frame(0x30CF, [0, 0, 85, 0, 1]);
    Expect(Mouse(Report(frame, frame)).Status == ReplyStatus.Invalid);
});
Check("Puerta de actividad y estabilización", () =>
{
    const long motion = 1000;
    Expect(!MonitorPolicy.CanRead(3000, null, 0));
    Expect(!MonitorPolicy.CanRead(1349, motion, 0));
    Expect(MonitorPolicy.CanRead(1350, motion, 0));
    Expect(!MonitorPolicy.CanRead(1500, motion, 2000));
    Expect(MonitorPolicy.CanRead(3499, motion, 0));
    Expect(!MonitorPolicy.CanRead(3500, motion, 0));
});
Check("Actualización manual acorta la espera y exige actividad reciente", () =>
{
    const long lastAttempt = 10_000;
    const long motion = 14_700;
    long manual = MonitorPolicy.EarliestAttempt(lastAttempt, 120, true, 0);
    long automatic = MonitorPolicy.EarliestAttempt(lastAttempt, 120, false, 0);
    Expect(manual == 15_000 && automatic == 130_000);
    Expect(!MonitorPolicy.CanRead(14_999, 14_500, manual));
    Expect(!MonitorPolicy.CanRead(15_050, null, manual));
    Expect(!MonitorPolicy.CanRead(15_050, 12_000, manual));
    Expect(MonitorPolicy.CanRead(15_050, motion, manual));
    Expect(!MonitorPolicy.CanRead(15_050, motion, automatic));
});
Check("Actualización manual respeta el aplazamiento por acceso ocupado", () =>
{
    const long accessUntil = 140_000;
    long eligible = MonitorPolicy.EarliestAttempt(10_000, 60, true, accessUntil);
    Expect(eligible == accessUntil);
    Expect(!MonitorPolicy.CanRead(139_999, 139_500, eligible));
    Expect(MonitorPolicy.CanRead(140_000, 139_500, eligible));
    Expect(MonitorPolicy.EarliestAttempt(null, 60, true, 0) == 0);
});
Check("Reconexión progresiva y espera por acceso ocupado", () =>
{
    Expect(MonitorPolicy.Backoff(1, 5, 120) == 5000);
    Expect(MonitorPolicy.Backoff(4, 5, 120) == 40_000);
    Expect(MonitorPolicy.Backoff(100, 5, 120) == 120_000);
    Expect(MonitorPolicy.Backoff(1, 120, 900) == 120_000);
    Expect(MonitorPolicy.Backoff(100, 120, 900) == 900_000);
});
Check("Colores de la bandeja en los límites", () =>
{
    Expect(MonitorPolicy.IconBand(null) == 0);
    Expect(MonitorPolicy.IconBand(0) == 1 && MonitorPolicy.IconBand(10) == 1);
    Expect(MonitorPolicy.IconBand(11) == 2 && MonitorPolicy.IconBand(30) == 2);
    Expect(MonitorPolicy.IconBand(31) == 3 && MonitorPolicy.IconBand(100) == 3);
});
Check("Alertas válidas, con escalada y sin repeticiones", () =>
{
    var alert = new BatteryAlert();
    Expect(alert.Observe(new(ReplyStatus.Invalid)) is null);
    Expect(alert.Observe(new(ReplyStatus.Level, 20)) == AlertLevel.Low);
    Expect(alert.Observe(new(ReplyStatus.Pending)) is null);
    Expect(alert.Observe(new(ReplyStatus.Level, 20)) is null);
    Expect(alert.Observe(new(ReplyStatus.Level, 5)) == AlertLevel.Critical);
    Expect(alert.Observe(new(ReplyStatus.Level, 0)) is null);
    Expect(alert.Observe(new(ReplyStatus.Level, 20, true)) is null);
    Expect(alert.Observe(new(ReplyStatus.Level, 20)) == AlertLevel.Low);
});
Check("Solo identificador del receptor .97", () =>
{
    Expect(HidBatteryService.IsReceiverPath(@"\\?\HID#VID_0E8D&PID_0703&MI_00&COL01#ID"));
    Expect(!HidBatteryService.IsReceiverPath(@"\\?\HID#VID_0E8D&PID_0880&MI_00&COL01#ID"));
    Expect(!HidBatteryService.IsReceiverPath(@"\\?\HID#VID_3151&PID_5007&MI_00&COL01#ID"));
    Expect(!HidBatteryService.IsReceiverPath(@"\\?\HID#VID_0E8D&PID_07030#ID"));
});
Check("Menú de bandeja recortado al monitor con coordenadas negativas", () =>
{
    var area = new Rectangle(-1920, -160, 1920, 1040);
    Expect(TrayMenu.KeepInside(new Point(-25, 850), new Size(240, 200), area) == new Point(-240, 680));
});
Check("Submenú se abre hacia dentro junto a los bordes de la pantalla", () =>
{
    var area = new Rectangle(-1920, 0, 1920, 1040);
    var size = new Size(125, 160);
    Expect(TraySubmenuItem.Place(new Rectangle(-245, 780, 245, 245), 920, size, area) == new Point(-369, 880));
    Expect(TraySubmenuItem.Place(new Rectangle(-1920, 100, 245, 245), 160, size, area) == new Point(-1676, 160));
});
Check("La preferencia de idioma conserva compatibilidad con configuraciones anteriores", () =>
{
    var previous = JsonSerializer.Deserialize<Preferences>("""{"Interval":120,"Notifications":false}""");
    Expect(previous is { Language: UiText.Spanish, Interval: 120, Notifications: false });
    var updated = previous! with { Language = UiText.English };
    Expect(JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(updated)) == updated);
    Expect(UiText.NormalizeLanguage("XX") == UiText.Spanish);
});
Check("Panel, avisos y menú ofrecen traducciones en inglés", () =>
{
    var es = UiText.Default;
    var en = new UiText(UiText.English);
    Expect(es["Language"] == "Idioma" && en["Language"] == "Language");
    Expect(es["RefreshNow"] == "Actualizar ahora" && en["RefreshNow"] == "Refresh now");
    foreach (var state in Enum.GetValues<LinkState>())
        Expect(en.Link(state) != es.Link(state));

    var time = new DateTimeOffset(2026, 1, 1, 14, 25, 51, TimeSpan.Zero);
    var spare = new BatteryDisplay(new Sample(81, true, time, 0), false);
    var display = new MonitorDisplay(LinkState.PossibleSleep, default, spare, true, false);
    Expect(en.Tooltip(display).Contains("Spare battery: 81% (Charging)\nLast updated: 14:25:51"));
    Expect(en.Time(spare with { Stale = true }) == "Last reading · 14:25:51");
    Expect(en.ManualPending(display) == "Move the .97 to refresh.");
    Expect(en.AlertLine(new BatteryNotice(5, AlertLevel.Critical), true) == "Spare battery: 5% (critical)");
});

Console.WriteLine($"{count} pruebas correctas.");
return 0;

void Check(string name, Action test)
{
    try { test(); }
    catch (Exception error) { throw new InvalidOperationException(name, error); }
    count++;
    Console.WriteLine($"OK  {name}");
}

static void Expect(bool condition)
{
    if (!condition)
        throw new InvalidOperationException("No coincide con el resultado esperado.");
}

static BatteryReply Mouse(byte[] report) => BatteryProtocol.Parse(report, BatteryTarget.Mouse);
static BatteryReply Spare(byte[] report) => BatteryProtocol.Parse(report, BatteryTarget.Spare);

static byte[] Frame(ushort command, byte[] payload, byte type = 0x5B) =>
    [5, type, (byte)(payload.Length + 2), 0, (byte)(command & 255), (byte)(command >> 8), .. payload];

static byte[] Report(params byte[][] frames)
{
    var report = new byte[62];
    report[0] = 0x14;
    report[1] = (byte)frames.Sum(frame => frame.Length);
    int offset = 3;
    foreach (var frame in frames)
    {
        frame.CopyTo(report, offset);
        offset += frame.Length;
    }
    return report;
}
