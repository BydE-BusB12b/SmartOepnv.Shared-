using System.IO.Ports;
using System.Text;

namespace SmartOepnv.IbisWagenbusUtility;

public enum IbisSerialMode
{
    /// <summary>Standard IBIS / Ibis Utility: 1200 7E2 + XOR-Paritätsbyte.</summary>
    Standard7E2,

    /// <summary>Gorba TFT klassisch: 1200 7E1, nur CR, kein XOR-Byte.</summary>
    Gorba7E1
}

/// <summary>Serielle Anbindung an den IBIS-Wagenbus-Wandler.</summary>
public sealed class IbisSerialLink : IDisposable
{
    private SerialPort? _port;
    private CancellationTokenSource? _rxCts;
    private Task? _rxTask;

    public event Action<byte[]>? BytesReceived;
    public event Action<string>? StatusChanged;
    public event Action<Exception>? ErrorOccurred;

    public bool IsOpen => _port?.IsOpen == true;
    public string? PortName => _port?.PortName;
    public IbisSerialMode Mode { get; private set; } = IbisSerialMode.Standard7E2;

    public static string[] GetPortNames() =>
        SerialPort.GetPortNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

    public void Open(string portName, IbisSerialMode mode = IbisSerialMode.Standard7E2)
    {
        Close();
        Mode = mode;

        if (mode == IbisSerialMode.Gorba7E1)
        {
            var gorba = CreatePort(portName, dataBits: 7, Parity.Even, StopBits.One);
            gorba.Open();
            _port = gorba;
            StatusChanged?.Invoke($"{portName} geöffnet (Gorba 1200 7E1, ohne XOR)");
            StartRx();
            return;
        }

        var port = CreatePort(portName, dataBits: 7, Parity.Even, StopBits.Two);
        try
        {
            port.Open();
        }
        catch (Exception)
        {
            try { port.Dispose(); } catch { /* ignore */ }
            port = CreatePort(portName, dataBits: 8, Parity.None, StopBits.One);
            port.Open();
            StatusChanged?.Invoke($"{portName} geöffnet (Fallback 8N1 @ 1200)");
            _port = port;
            StartRx();
            return;
        }

        _port = port;
        StatusChanged?.Invoke($"{portName} geöffnet (1200 7E2)");
        StartRx();
    }

    private static SerialPort CreatePort(string portName, int dataBits, Parity parity, StopBits stopBits) =>
        new(portName)
        {
            BaudRate = 1200,
            DataBits = dataBits,
            Parity = parity,
            StopBits = stopBits,
            Handshake = Handshake.None,
            ReadTimeout = 200,
            WriteTimeout = 2000,
            Encoding = Encoding.ASCII,
            DtrEnable = true,
            RtsEnable = true
        };

    public void Close()
    {
        _rxCts?.Cancel();
        try { _rxTask?.Wait(500); } catch { /* ignore */ }
        _rxCts?.Dispose();
        _rxCts = null;
        _rxTask = null;

        if (_port != null)
        {
            try
            {
                if (_port.IsOpen)
                {
                    _port.Close();
                }
            }
            catch { /* ignore */ }
            _port.Dispose();
            _port = null;
            StatusChanged?.Invoke("Port geschlossen");
        }
    }

    public void Send(byte[] telegram)
    {
        if (_port is not { IsOpen: true })
        {
            throw new InvalidOperationException("Kein COM-Port geöffnet.");
        }

        if (telegram.Length == 0)
        {
            return;
        }

        _port.Write(telegram, 0, telegram.Length);
        StatusChanged?.Invoke($"TX {telegram.Length} Bytes");
    }

    public void SendMany(IEnumerable<byte[]> telegrams, int delayMs = 50)
    {
        foreach (var t in telegrams)
        {
            Send(t);
            if (delayMs > 0)
            {
                Thread.Sleep(delayMs);
            }
        }
    }

    private void StartRx()
    {
        _rxCts = new CancellationTokenSource();
        var token = _rxCts.Token;
        var port = _port!;
        _rxTask = Task.Run(() =>
        {
            var buffer = new byte[512];
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!port.IsOpen)
                    {
                        break;
                    }

                    var available = port.BytesToRead;
                    if (available <= 0)
                    {
                        Thread.Sleep(40);
                        continue;
                    }

                    var toRead = Math.Min(available, buffer.Length);
                    var read = port.Read(buffer, 0, toRead);
                    if (read > 0)
                    {
                        var chunk = new byte[read];
                        Array.Copy(buffer, chunk, read);
                        BytesReceived?.Invoke(chunk);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (TimeoutException)
                {
                    // normal
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    ErrorOccurred?.Invoke(ex);
                    Thread.Sleep(100);
                }
            }
        }, token);
    }

    public void Dispose() => Close();
}
