using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Oeps.RawMaterialSticker.Core;
using Oeps.RawMaterialSticker.Core.Camera;
using Oeps.RawMaterialSticker.Core.Configuration;
using Oeps.RawMaterialSticker.Core.Data;
using Oeps.RawMaterialSticker.Core.Printing;
using Scanner.Client;

namespace Oeps.RawMaterialSticker.Tests;

public static class CameraTests
{
    private const string Json = """{"label_version":1,"oeps_pn":"OEPSA010123","lot":"0926-TRAY-Q5R2","quantity":"00001.5"}""";

    [Test]
    public static void CameraPreservesLotAndRecalculatesBarcodeForEditedQuantity()
    {
        var reading = CameraReading.Parse(Json);
        Assert.Equal(1.5m, reading.Quantity);
        var request = reading.Request("MPN", 42);
        var rendered = new LabelRenderer(LabelRenderer.BuiltInTemplate).Render(request);
        Assert.Equal("0926-TRAY-Q5R2", rendered.LotCode);
        Assert.Equal(L3Barcode.Encode("OEPSA010123", reading.Lot, "0000042"), rendered.BarcodePayload);
        var session = new OperationSession();
        session.UseScannedLot(request); session.Revalidate([]);
        Assert.Equal("Q5R2", session.RandomCode);
        Assert.Equal("09", session.Month); Assert.Equal("2026", session.Year); Assert.Equal("Tray", session.Packaging);
        foreach (var (date, monthUnknown, yearUnknown) in new[] { ("0026", true, false), ("0900", false, true), ("0000", true, true) })
        {
            var unknown = CameraReading.Parse(Json.Replace("0926", date));
            session.UseScannedLot(unknown.Request("MPN", 1));
            Assert.Equal(monthUnknown, session.MonthUnavailable); Assert.Equal(yearUnknown, session.YearUnavailable);
            Assert.Equal(date + "-TRAY-Q5R2", LabelValues.FormatLot(unknown.Request("MPN", 1)));
        }
    }

    [Test]
    public static void UnusableCameraDataDoesNotBecomeALabel()
    {
        foreach (var json in new[] { "{}", "not json", Json.Replace("0926", "1326"), Json.Replace("0926", "0919"),
            Json.Replace("0926", "0999"), Json.Replace("TRAY", "FAKE"), Json.Replace("Q5R2", "q5r2"),
            Json.Replace("00001.5", "0"), Json.Replace("00001.5", "-1"), Json.Replace("00001.5", "10000000000"),
            Json.Replace("00001.5", "0.1234567891"), Json.Replace("OEPSA010123", "OEPS 01 0123"), Json.Replace(":1,", ":2,") })
            Assert.Throws<Exception>(() => CameraReading.Parse(json));
    }

    [Test]
    public static void MpnRequiresAnUnambiguousDatabasePairAndUsesLatestExpensiveFlag()
    {
        var reading = CameraReading.Parse(Json);
        var first = new Component(reading.OepsPn, "MPN-1", IsExpensive: true);
        var second = first with { Mpn = "MPN-2" };
        Assert.Equal(first, reading.ResolveComponent([first], null));
        Assert.Throws<ArgumentException>(() => reading.ResolveComponent([], null));
        Assert.Throws<ArgumentException>(() => reading.ResolveComponent([first, second], null));
        Assert.Throws<ArgumentException>(() => reading.ResolveComponent([first, second], new("OEPS000000", "MPN-1")));
        Assert.Equal(second, reading.ResolveComponent([first, second], second with { IsExpensive = false }));
    }

    [Test]
    public static void OnlyConsecutiveDuplicatesAreSuppressedAndActionsAllowRescanning()
    {
        var inbox = new CameraInbox();
        Assert.True(inbox.Accept(Json)); Assert.False(inbox.Accept(Json));
        Assert.True(inbox.Accept(Json.Replace("Q5R2", "ABCD")));
        Assert.True(inbox.Accept(Json.Replace("Q5R2", "EFGH")));
        Assert.True(inbox.Accept(Json));
        Assert.False(inbox.Accept(Json));
        inbox.ActionPressed(); Assert.True(inbox.Accept(Json)); Assert.False(inbox.Accept(Json));
    }

    [Test]
    public static void CameraPreferencesRoundTripAndOldSettingsUseLocalDefaults()
    {
        var folder = Path.Combine(Path.GetTempPath(), "oeps-camera-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "user-settings.json");
        try
        {
            Assert.False(UserSettings.Load(path).CameraExternal);
            Assert.Equal(8765, UserSettings.Load(path).CameraPort);
            new UserSettings { CameraExternal = true, CameraIp = "192.168.1.50", CameraPort = 9000 }.Save(path);
            var saved = UserSettings.Load(path);
            Assert.True(saved.CameraExternal); Assert.Equal("192.168.1.50", saved.CameraIp); Assert.Equal(9000, saved.CameraPort);
            File.WriteAllText(path, "{\"cameraPort\":0,\"cameraIp\":null}");
            Assert.Equal(8765, UserSettings.Load(path).CameraPort); Assert.Equal("", UserSettings.Load(path).CameraIp);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Test]
    public static async Task SuppliedSdkReceivesOriginalJsonAndDisconnectsFromWebSocket()
    {
        foreach (var local in new[] { true, false })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var token = timeout.Token;
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var tcp = await listener.AcceptTcpClientAsync(token);
                await using var stream = tcp.GetStream();
                var headers = new StringBuilder(); var buffer = new byte[1];
                while (!headers.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    Assert.True(headers.Length < 8192 && await stream.ReadAsync(buffer, token) == 1);
                    headers.Append((char)buffer[0]);
                }
                var key = headers.ToString().Split("\r\n").Single(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)).Split(':', 2)[1].Trim();
                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), token);
                using var socket = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(30));
                for (var i = 0; i < 2; i++)
                    await socket.SendAsync(Encoding.UTF8.GetBytes(Json).AsMemory(), WebSocketMessageType.Text, true, token);
                try
                {
                    var close = await socket.ReceiveAsync(new byte[1024].AsMemory(), token);
                    Assert.Equal(WebSocketMessageType.Close, close.MessageType);
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Done", token);
                }
                catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                { /* The supplied SDK cancels its receive loop and closes TCP without a close frame. */ }
            }, token);
            await using var client = new ScannerConnection();
            var received = new List<string>();
            var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.JsonReceived += json => { received.Add(json); if (received.Count == 2) both.TrySetResult(); };
            await client.ConnectAsync(local ? null : "127.0.0.1", port, local, token);
            await both.Task.WaitAsync(token);
            Assert.True(client.Connected); Assert.True(received.All(json => json == Json));
            await client.CloseAsync().WaitAsync(token);
            Assert.False(client.Connected);
            await server;
        }
    }
}
