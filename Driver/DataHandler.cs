using NLog;
using NMEA_FPU_DRIVER.Config;
using Org.BouncyCastle.Crypto.Signers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NMEA_FPU_DRIVER.Driver
{
    public sealed class DataHandler : IDisposable
    {
        private Dictionary<string, FPU> _devices = new Dictionary<string, FPU>();
        private NmeaDriverConfig _cfg;
        private CancellationTokenSource _cts;
        private Task _loop;

        private UdpDriver _driver;
        private UInt16 _heartbeat;

        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public DataHandler(UdpDriver driver, Action<Action<NmeaSentence>> subscribe, Dictionary<string, FPU> devices, NmeaDriverConfig cfg)
        {
            _driver = driver;
            subscribe(HandleSentence);
            _devices = devices;
            _cfg = cfg;
        }

        private void HandleSentence(NmeaSentence s)
        {
            Console.WriteLine($"{s.DeviceName}: {s.Type}, {s.Talker} || {s.Raw}");

            if (s.Type == "TSS1")
            {
                _devices[s.DeviceName].ParseTSS1(s.Raw);
            }
            else if (s.Type == "HDT")
            {
                _devices[s.DeviceName].ParseHDT(s.Raw);
            }
            else if (s.Type == "GGA")
            {
                _devices[s.DeviceName].ParseGGA(s.Raw);
            }
            else if (s.Type == "ZDA")
            {
                _devices[s.DeviceName].ParseZDA(s.Raw);
            }

        }

        public void Start()
        {
            if (_loop != null) return;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }

        public async Task StopAsync()
        {
            if (_cts == null) return;
            try { _cts.Cancel(); } catch { }
            var t = _loop; _loop = null;
            if (t != null) { try { await t.ConfigureAwait(false); } catch { }}
            _cts.Dispose(); _cts = null;
        }

        public void Dispose() { try { StopAsync().GetAwaiter().GetResult(); } catch { } }

        private async Task LoopAsync(CancellationToken ct)
        {
            await Task.Delay(_cfg.DataHandler.InitialDelayMs);
            var nextDue = DateTime.UtcNow + TimeSpan.FromMilliseconds(_cfg.DataHandler.TickIntervalMs);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    
                }
                catch (Exception ex) 
                {
                    _log.Warn($"Data handler tick failed. {ex}");
                }

                nextDue += TimeSpan.FromMilliseconds(_cfg.DataHandler.TickIntervalMs);
                var delay = nextDue - DateTime.UtcNow;

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }
}
