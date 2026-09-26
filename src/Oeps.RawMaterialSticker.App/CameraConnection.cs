using Scanner.Client;

namespace Oeps.RawMaterialSticker.App;

internal interface ICameraConnection : IAsyncDisposable
{
    event Action<string>? JsonReceived;
    event Action<string>? StateChanged;
    Task ConnectAsync(string? ip, int port, bool local, CancellationToken token);
    Task CloseAsync();
}

internal sealed class CameraConnection : ICameraConnection
{
    private readonly ScannerConnection _scanner = new();
    public event Action<string>? JsonReceived { add => _scanner.JsonReceived += value; remove => _scanner.JsonReceived -= value; }
    public event Action<string>? StateChanged { add => _scanner.StateChanged += value; remove => _scanner.StateChanged -= value; }
    public Task ConnectAsync(string? ip, int port, bool local, CancellationToken token) => _scanner.ConnectAsync(ip, port, local, token);
    public Task CloseAsync() => _scanner.CloseAsync();
    public ValueTask DisposeAsync() => _scanner.DisposeAsync();
}
