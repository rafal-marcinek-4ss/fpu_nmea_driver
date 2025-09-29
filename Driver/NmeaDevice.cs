using NMEA_FPU_DRIVER.Config;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using NLog;


namespace NMEA_FPU_DRIVER.Driver
{

    public enum NmeaDeviceStatus { Disconnected, Connecting, Connected, Faulted }

    public sealed class NmeaUdpDevice : IDisposable
    {

        private readonly NmeaDeviceConfig _cfg;
        private readonly NmeaSettings _nmea;

        private CancellationTokenSource _cts;
        private Task _loopTask;
        private Task _serviceTask;

        private readonly ConcurrentQueue<NmeaSentence> _outgoing = new ConcurrentQueue<NmeaSentence>();
        private readonly object _stateLock = new object();
        private volatile NmeaDeviceStatus _status = NmeaDeviceStatus.Disconnected;
        private DateTimeOffset _lastReceive = DateTimeOffset.MinValue;

        public event Action<NmeaDeviceStatus> OnStatusChanged;
        public event Action<NmeaSentence> OnSentence;
        public event Action<String> OnMessageReceived;

        public string Name { get { return _cfg.Name; } }

        private static readonly object s_ConfigWriteLock = new object();

        private Logger _log = LogManager.GetCurrentClassLogger();


        public NmeaUdpDevice(NmeaDeviceConfig cfg, NmeaSettings nmea)
        {
            _cfg = cfg;
            _nmea = nmea;
        }

        public Task StartAsync(CancellationToken ct = default(CancellationToken))
        {
            if (_loopTask != null) return Task.CompletedTask;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _loopTask = Task.Run(() => RunAsync(_cts.Token));
            _serviceTask = Task.Run(() => ServiceLoopAsync(_cts.Token));
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (_cts == null) return;
            try { _cts.Cancel(); } catch { }
            if (_loopTask != null) await Task.WhenAll(Helpers.Quiet(_loopTask), Helpers.Quiet(_serviceTask));
            _loopTask = null;
            _serviceTask = null;
            SetStatus(NmeaDeviceStatus.Disconnected);
        }

        public void Dispose()
        {
            try { StopAsync().GetAwaiter().GetResult(); } catch { }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            var reconnect = _cfg.Reconnect;
            var delay = TimeSpan.FromMilliseconds(Math.Max(100, reconnect.InitialDelayMs));
            var maxDelay = TimeSpan.FromMilliseconds(Math.Max(reconnect.InitialDelayMs, reconnect.MaxDelayMs));
            var rnd = new Random();

            while (!ct.IsCancellationRequested)
            {

                UdpClient client = null;
                try
                {
                    SetStatus(NmeaDeviceStatus.Connecting);

                    IPAddress localIp = IPAddress.Any;

                    if (!string.IsNullOrWhiteSpace(_cfg.Socket.LocalAddress))
                    {
                        IPAddress tmp;
                        if (IPAddress.TryParse(_cfg.Socket.LocalAddress, out tmp)) localIp = tmp;
                        else
                        {
                            var he = Dns.GetHostEntry(_cfg.Socket.LocalAddress);
                            localIp = he.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? IPAddress.Any;
                        }
                    }


                    var localEp = new IPEndPoint(localIp, _cfg.Port);
                    client = new UdpClient(localEp);
                    client.Client.ReceiveTimeout = _cfg.Socket.ReceiveTimeoutMs;
                    client.Client.SendTimeout = _cfg.Socket.SendTimeoutMs;

                    IPAddress expectedSenderIp = null;
                    if (!string.IsNullOrEmpty(_cfg.Host))
                    {
                        IPAddress ip;
                        if (IPAddress.TryParse(_cfg.Host, out ip)) expectedSenderIp = ip;
                        else
                        {
                            try
                            {
                                var he = await Dns.GetHostEntryAsync(_cfg.Host).ConfigureAwait(false);
                                expectedSenderIp = he.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? he.AddressList.FirstOrDefault();
                            }
                            catch (Exception rex)
                            {
                                _log.Warn($"COULDN'T PARSE SENDER IP {rex}");
                            }
                        }
                    }

                    _lastReceive = DateTimeOffset.UtcNow;
                    delay = TimeSpan.FromMilliseconds(Math.Max(100, reconnect.InitialDelayMs));


                    while (!ct.IsCancellationRequested)
                    {
                        var recvTask = client.ReceiveAsync();
                        var completed = await Task.WhenAny(recvTask, Task.Delay(_cfg.Socket.ReceiveTimeoutMs, ct)).ConfigureAwait(false);

                        if (completed != recvTask)
                        {
                            continue;
                        }

                        UdpReceiveResult res;
                        try { res = await recvTask.ConfigureAwait(false); }
                        catch (AggregateException aex) { _log.Warn($"AGGREGATE ERROR PARSING GETTING RESULT {aex}"); }
                        catch (Exception ex) { _log.Warn($"ERROR PARSING GETTING RESULT {ex}"); continue; }

                        if (expectedSenderIp != null && !res.RemoteEndPoint.Address.Equals(expectedSenderIp))
                            continue;

                        bool anyAccepted = false;
                        //var lines = ExtractNmeaSentences(res.Buffer);
                        var lines = ExtractFrames(res.Buffer);
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrEmpty(line)) continue;

                            string talker = null, type = null;
                            bool ok = true;
                            bool acceptedThisLine = false;

                            if (line[0] == '$' || line[0] == '!')
                            {
                                ok = Helpers.ValidateCheckSum(line, out talker, out type);
                                if (!ok && !_nmea.EmitInvalid) continue;

                                var snt = new NmeaSentence
                                {
                                    DeviceName = Name,
                                    Timestamp = DateTimeOffset.UtcNow,
                                    Raw = line,
                                    ChecksumValid = ok,
                                    Talker = talker,
                                    Type = type
                                };
                                _outgoing.Enqueue(snt);
                                acceptedThisLine = true;
                            }
                            else if (line[0] == ':')
                            {
                                // TSS1 legacy
                                if (!TryParseTss1Legacy(line, out var aa, out var seq, out var heave, out var roll, out var pitch))
                                    continue;

  
                                var snt = new NmeaSentence
                                {
                                    DeviceName = Name,
                                    Timestamp = DateTimeOffset.UtcNow,
                                    Raw = line,      
                                    ChecksumValid = true, 
                                    Talker = "P",    
                                    Type = "TSS1"
                                };
                                _outgoing.Enqueue(snt);
                                acceptedThisLine = true;

                            }
                            else
                            {
                                continue; 
                            }

                            if (acceptedThisLine)
                                anyAccepted = true;
                        }

                        if (anyAccepted)
                        {
                            var beat = OnMessageReceived;
                            if (beat != null) { try { beat(Name); } catch { } }
                            _lastReceive = DateTimeOffset.UtcNow;
                            SetStatus(NmeaDeviceStatus.Connected);
                        }
                    }

                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SetStatus(NmeaDeviceStatus.Faulted);
                    _log.Warn($"MESSAGE NOT RECEIVED AND PARSED {ex}");
                }
                finally
                {
                    try { if (client != null) client.Close(); } catch { }
                }

                if (!ct.IsCancellationRequested)
                {
                    SetStatus(NmeaDeviceStatus.Disconnected);

                    var jitter = TimeSpan.FromMilliseconds(rnd.Next(0, Math.Max(0, _cfg.Reconnect.JitterMs)));
                    var toWait = delay + jitter;
                    await Task.Delay(toWait, ct).ConfigureAwait(false);
                    var next = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * _cfg.Reconnect.Multiplier);
                    delay = next <= maxDelay ? next : maxDelay;
                }

            }
        }


        private static IEnumerable<string> ExtractFrames(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0) yield break;

            var s = Encoding.ASCII.GetString(buffer);

            // 1) NMEA ($/!) z checksum
            var rxNmea = new Regex(@"(?:(?:\$|!)[^\r\n\$]*?\*[0-9A-Fa-f]{2})", RegexOptions.Compiled);
            foreach (Match m in rxNmea.Matches(s))
            {
                var sentence = m.Value.Trim();
                if (sentence.Length > 0) yield return sentence;
            }

            // 2) TSS1 legacy (":aabbbb shhhhxsrrrr spppp") 
            var lines = s.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var rxTss = new Regex(@"^:\d{2}\d{4} [\+\-]\d{4}x[\+\-]\d{4} [\+\-]\d{4}$", RegexOptions.Compiled);
            foreach (var line in lines)
            {
                var t = line.Trim();
                if (t.Length == 0) continue;
                if (t[0] == ':' && rxTss.IsMatch(t))
                    yield return t;
            }
        }
        private static bool TryParseTss1Legacy(
    string raw, out int aa, out int seq,
    out double heave_m, out double roll_deg, out double pitch_deg)
        {
            aa = seq = 0; heave_m = roll_deg = pitch_deg = double.NaN;

            
            var rx = new Regex(@"^:(\d{2})(\d{4}) ([\+\-])(\d{4})x([\+\-])(\d{4}) ([\+\-])(\d{4})$",
                               RegexOptions.Compiled);
            var m = rx.Match(raw);
            if (!m.Success) return false;

            aa = int.Parse(m.Groups[1].Value);
            seq = int.Parse(m.Groups[2].Value);

            int h = int.Parse(m.Groups[4].Value); // cm
            int r = int.Parse(m.Groups[6].Value); // 0.01 deg
            int p = int.Parse(m.Groups[8].Value);

            heave_m = (m.Groups[3].Value == "-" ? -h : h) / 100.0;
            roll_deg = (m.Groups[5].Value == "-" ? -r : r) / 100.0;
            pitch_deg = (m.Groups[7].Value == "-" ? -p : p) / 100.0;
            return true;
        }

        private async Task ServiceLoopAsync(CancellationToken ct)
        {
            var interval = TimeSpan.FromMilliseconds(Math.Max(200, _cfg.ServiceIntervalMs));
            var sw = Stopwatch.StartNew();
            while (!ct.IsCancellationRequested)
            {
                var emitted = 0;
                NmeaSentence sentence;

                while (emitted < 200 && _outgoing.TryDequeue(out sentence))
                {
                    try { var h = OnSentence; if (h != null) h(sentence); }
                    catch (Exception ex) { _log.Warn(ex); }
                    emitted++;
                }

                if(_status == NmeaDeviceStatus.Connected)
                {
                    var silence = DateTimeOffset.UtcNow - _lastReceive;
                    if (silence.TotalMilliseconds > _cfg.HeartbeatTimeoutMs)
                    {
                        SetStatus(NmeaDeviceStatus.Disconnected);
                    }
                }

                var toWait = interval - sw.Elapsed;
                if (toWait < TimeSpan.Zero) toWait = TimeSpan.Zero;
                await Task.Delay(toWait, ct).ConfigureAwait(false);
                sw.Restart();
            }
        }

        private void SetStatus(NmeaDeviceStatus s)
        {
            lock (_stateLock)
            {
                if (_status == s) return;
                _status = s;
            }
            try { var h = OnStatusChanged; if (h != null) h(s); }
            catch (Exception ex) { _log.Warn(ex); };
        }

    }
    
    
}
