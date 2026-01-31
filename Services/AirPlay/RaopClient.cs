using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace AirPlayStreamer.Services.AirPlay;

/// <summary>
/// RAOP (Remote Audio Output Protocol) client for AirPlay streaming
/// Implements the RTSP-based protocol for AirPlay audio
/// </summary>
public class RaopClient : IDisposable
{
    private TcpClient? _rtspClient;
    private NetworkStream? _rtspStream;
    private UdpClient? _audioClient;
    private UdpClient? _controlClient;
    private UdpClient? _timingClient;

    private readonly string _host;
    private readonly int _port;
    private int _cseq = 0;
    private string? _sessionId;
    private int _serverPort;
    private int _controlPort;
    private int _timingPort;

    private bool _isStreaming;
    private CancellationTokenSource? _streamingCts;

    public event EventHandler<string>? OnError;
    public event EventHandler? OnConnected;
    public event EventHandler? OnDisconnected;

    public bool IsConnected => _rtspClient?.Connected ?? false;
    public bool IsStreaming => _isStreaming;

    public RaopClient(string host, int port = 7000)
    {
        _host = host;
        _port = port;
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[RAOP] Connecting to {_host}:{_port}");

            _rtspClient = new TcpClient();
            await _rtspClient.ConnectAsync(_host, _port, cancellationToken);
            _rtspStream = _rtspClient.GetStream();

            // Send OPTIONS to verify connection
            var optionsResponse = await SendRtspRequestAsync("OPTIONS", "*");
            if (optionsResponse == null || !optionsResponse.StartsWith("RTSP/1.0 200"))
            {
                throw new Exception("OPTIONS request failed");
            }

            System.Diagnostics.Debug.WriteLine("[RAOP] OPTIONS successful");
            OnConnected?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RAOP] Connect error: {ex.Message}");
            OnError?.Invoke(this, ex.Message);
            return false;
        }
    }

    public async Task<bool> SetupSessionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate random ports for audio/control/timing
            var random = new Random();
            var localAudioPort = random.Next(6000, 6999);
            var localControlPort = localAudioPort + 1;
            var localTimingPort = localAudioPort + 2;

            // Setup UDP clients
            _audioClient = new UdpClient(localAudioPort);
            _controlClient = new UdpClient(localControlPort);
            _timingClient = new UdpClient(localTimingPort);

            // ANNOUNCE - describe the audio format
            var sdp = BuildSdp();
            var announceHeaders = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/sdp",
                ["Content-Length"] = sdp.Length.ToString()
            };

            var announceResponse = await SendRtspRequestAsync("ANNOUNCE", $"rtsp://{_host}/{Guid.NewGuid()}", announceHeaders, sdp);
            if (announceResponse == null || !announceResponse.Contains("200"))
            {
                System.Diagnostics.Debug.WriteLine($"[RAOP] ANNOUNCE response: {announceResponse}");
                // Some devices might not require ANNOUNCE, continue anyway
            }

            // SETUP - establish the streaming session
            var setupHeaders = new Dictionary<string, string>
            {
                ["Transport"] = $"RTP/AVP/UDP;unicast;interleaved=0-1;mode=record;control_port={localControlPort};timing_port={localTimingPort}"
            };

            var setupResponse = await SendRtspRequestAsync("SETUP", $"rtsp://{_host}/{Guid.NewGuid()}", setupHeaders);
            if (setupResponse == null)
            {
                throw new Exception("SETUP request failed");
            }

            // Parse server ports from response
            ParseSetupResponse(setupResponse);

            System.Diagnostics.Debug.WriteLine($"[RAOP] SETUP successful - Server port: {_serverPort}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RAOP] Setup error: {ex.Message}");
            OnError?.Invoke(this, ex.Message);
            return false;
        }
    }

    public async Task<bool> StartStreamingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // RECORD - start streaming
            var recordHeaders = new Dictionary<string, string>
            {
                ["Range"] = "npt=0-",
                ["RTP-Info"] = "seq=0;rtptime=0"
            };

            var recordResponse = await SendRtspRequestAsync("RECORD", $"rtsp://{_host}/{_sessionId}", recordHeaders);
            if (recordResponse == null || !recordResponse.Contains("200"))
            {
                System.Diagnostics.Debug.WriteLine($"[RAOP] RECORD response: {recordResponse}");
            }

            _isStreaming = true;
            _streamingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            System.Diagnostics.Debug.WriteLine("[RAOP] Streaming started");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RAOP] Start streaming error: {ex.Message}");
            OnError?.Invoke(this, ex.Message);
            return false;
        }
    }

    public async Task SendAudioPacketAsync(byte[] audioData, uint timestamp, ushort sequenceNumber)
    {
        if (!_isStreaming || _audioClient == null || _serverPort == 0)
            return;

        try
        {
            // Build RTP packet
            var rtpPacket = BuildRtpPacket(audioData, timestamp, sequenceNumber);

            await _audioClient.SendAsync(rtpPacket, rtpPacket.Length, _host, _serverPort);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RAOP] Send audio error: {ex.Message}");
        }
    }

    public async Task StopStreamingAsync()
    {
        try
        {
            _isStreaming = false;
            _streamingCts?.Cancel();

            if (_rtspClient?.Connected == true)
            {
                // TEARDOWN - end the session
                await SendRtspRequestAsync("TEARDOWN", $"rtsp://{_host}/{_sessionId}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RAOP] Stop error: {ex.Message}");
        }
        finally
        {
            OnDisconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<string?> SendRtspRequestAsync(string method, string uri, Dictionary<string, string>? headers = null, string? body = null)
    {
        if (_rtspStream == null)
            return null;

        _cseq++;

        var request = new StringBuilder();
        request.AppendLine($"{method} {uri} RTSP/1.0");
        request.AppendLine($"CSeq: {_cseq}");
        request.AppendLine("User-Agent: AirPlayStreamer/1.0");

        if (_sessionId != null)
        {
            request.AppendLine($"Session: {_sessionId}");
        }

        if (headers != null)
        {
            foreach (var header in headers)
            {
                request.AppendLine($"{header.Key}: {header.Value}");
            }
        }

        request.AppendLine();

        if (body != null)
        {
            request.Append(body);
        }

        var requestBytes = Encoding.ASCII.GetBytes(request.ToString());
        await _rtspStream.WriteAsync(requestBytes);
        await _rtspStream.FlushAsync();

        // Read response
        var buffer = new byte[4096];
        var bytesRead = await _rtspStream.ReadAsync(buffer);
        var response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

        System.Diagnostics.Debug.WriteLine($"[RAOP] {method} response:\n{response}");
        return response;
    }

    private string BuildSdp()
    {
        // SDP for ALAC audio (Apple Lossless)
        return $@"v=0
o=iTunes {DateTimeOffset.UtcNow.ToUnixTimeSeconds()} 0 IN IP4 {GetLocalIp()}
s=iTunes
c=IN IP4 {_host}
t=0 0
m=audio 0 RTP/AVP 96
a=rtpmap:96 AppleLossless
a=fmtp:96 352 0 16 40 10 14 2 255 0 0 44100
";
    }

    private void ParseSetupResponse(string response)
    {
        // Parse Session header
        var sessionMatch = System.Text.RegularExpressions.Regex.Match(response, @"Session:\s*(\S+)");
        if (sessionMatch.Success)
        {
            _sessionId = sessionMatch.Groups[1].Value.Split(';')[0];
        }

        // Parse Transport header for server port
        var transportMatch = System.Text.RegularExpressions.Regex.Match(response, @"server_port=(\d+)");
        if (transportMatch.Success)
        {
            _serverPort = int.Parse(transportMatch.Groups[1].Value);
        }
        else
        {
            // Default to standard AirPlay audio port
            _serverPort = 6000;
        }
    }

    private byte[] BuildRtpPacket(byte[] audioData, uint timestamp, ushort sequenceNumber)
    {
        var packet = new byte[12 + audioData.Length];

        // RTP header (12 bytes)
        packet[0] = 0x80; // Version 2, no padding, no extension, no CSRC
        packet[1] = 0x60; // Payload type 96 (dynamic)
        packet[2] = (byte)(sequenceNumber >> 8);
        packet[3] = (byte)(sequenceNumber & 0xFF);
        packet[4] = (byte)(timestamp >> 24);
        packet[5] = (byte)(timestamp >> 16);
        packet[6] = (byte)(timestamp >> 8);
        packet[7] = (byte)(timestamp & 0xFF);

        // SSRC (random identifier)
        var ssrc = 0x12345678u;
        packet[8] = (byte)(ssrc >> 24);
        packet[9] = (byte)(ssrc >> 16);
        packet[10] = (byte)(ssrc >> 8);
        packet[11] = (byte)(ssrc & 0xFF);

        // Audio data
        Buffer.BlockCopy(audioData, 0, packet, 12, audioData.Length);

        return packet;
    }

    private static string GetLocalIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endpoint = socket.LocalEndPoint as IPEndPoint;
            return endpoint?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    public void Dispose()
    {
        _streamingCts?.Cancel();
        _streamingCts?.Dispose();
        _audioClient?.Dispose();
        _controlClient?.Dispose();
        _timingClient?.Dispose();
        _rtspStream?.Dispose();
        _rtspClient?.Dispose();
    }
}
