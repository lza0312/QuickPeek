using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace QuickPeek.Injector;

public enum InjectorMessageType
{
    Ping = 0,
    GetCurrentDirectory = 1,
    GetHoveredFilePath = 2,
    SubscribeEvents = 3,
    UnsubscribeEvents = 4,
    EventNotification = 5
}

public class InjectorMessage
{
    public InjectorMessageType Type { get; set; }
    public string? Payload { get; set; }
    public string? ResponseId { get; set; }
}

public class InjectorCommunication : IDisposable
{
    private const string PipeNamePrefix = "QuickPeek_Pipe_";
    private readonly string _pipeName;
    private NamedPipeClientStream? _clientPipe;
    private NamedPipeServerStream? _serverPipe;
    private bool _isRunning;
    private bool _isDisposed;
    private Thread? _listenThread;
    private readonly CancellationTokenSource _cts = new();

    public event EventHandler<InjectorMessage>? MessageReceived;
    public bool IsConnected => (_clientPipe?.IsConnected ?? false) || (_serverPipe?.IsConnected ?? false);

    public InjectorCommunication()
    {
        _pipeName = PipeNamePrefix + Guid.NewGuid().ToString("N");
    }

    private PipeStream? GetPipe()
    {
        return (PipeStream?)_clientPipe ?? _serverPipe;
    }

    public async Task ConnectAsync(int timeoutMs = 3000)
    {
        try
        {
            _clientPipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            await _clientPipe.ConnectAsync(timeoutMs);
            _isRunning = true;
            StartListening();
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                "无法连接到注入的 DLL。请确认 DLL 已成功注入到 explorer.exe 中。");
        }
    }

    public void StartServer()
    {
        _serverPipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Message, PipeOptions.Asynchronous);
        _serverPipe.BeginWaitForConnection(async ar =>
        {
            try
            {
                _serverPipe!.EndWaitForConnection(ar);
                _isRunning = true;
                StartListening();
                await SendMessageAsync(new InjectorMessage
                {
                    Type = InjectorMessageType.Ping,
                    Payload = "Hello from QuickPeek!"
                });
            }
            catch
            {
            }
        }, null);
    }

    private void StartListening()
    {
        _listenThread = new Thread(() =>
        {
            var pipe = GetPipe();
            if (pipe == null) return;

            var buffer = new byte[8192];
            while (_isRunning && !_cts.IsCancellationRequested)
            {
                try
                {
                    if (!pipe.IsConnected)
                    {
                        break;
                    }

                    int read = pipe.Read(buffer, 0, buffer.Length);
                    if (read == 0) continue;

                    var json = Encoding.UTF8.GetString(buffer, 0, read);
                    var message = JsonSerializer.Deserialize<InjectorMessage>(json);
                    if (message != null)
                    {
                        MessageReceived?.Invoke(this, message);
                    }
                }
                catch (IOException)
                {
                    break;
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }
        })
        {
            IsBackground = true
        };
        _listenThread.Start();
    }

    public async Task SendMessageAsync(InjectorMessage message)
    {
        var pipe = GetPipe();
        if (pipe == null || !pipe.IsConnected) return;

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await pipe.WriteAsync(bytes, 0, bytes.Length);
        await pipe.FlushAsync();
    }

    public void SendMessage(InjectorMessage message)
    {
        var pipe = GetPipe();
        if (pipe == null || !pipe.IsConnected) return;

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        pipe.Write(bytes, 0, bytes.Length);
        pipe.Flush();
    }

    public void Disconnect()
    {
        _isRunning = false;
        _cts.Cancel();

        try { _serverPipe?.Disconnect(); } catch { }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Disconnect();

        try { _clientPipe?.Dispose(); } catch { }
        try { _serverPipe?.Dispose(); } catch { }
        _cts.Dispose();

        GC.SuppressFinalize(this);
    }
}
