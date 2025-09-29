using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NMEA_FPU_DRIVER.Config;


namespace NMEA_FPU_DRIVER.Driver
{
    public sealed class UdpDriver : IDisposable
    {
        private readonly NmeaDriverConfig _config;

        public ConcurrentDictionary<string, NmeaUdpDevice> Devices = new ConcurrentDictionary<string, NmeaUdpDevice>();

        public event Action<NmeaDeviceStatus, string> OnDeviceStatusChanged;
        public event Action<NmeaSentence> OnSentence;
        public event Action<string> OnDeviceBeat;

        public UdpDriver(NmeaDriverConfig config)
        {
            _config = config;

            foreach(var d in _config.Devices)
            {
                var merged = Merge(d, _config);
                var dev = new NmeaUdpDevice(merged, _config.Nmea); ;
                dev.OnStatusChanged += (s) => { var h = OnDeviceStatusChanged; if (h != null) h(s, merged.Name); };
                dev.OnSentence += (s) => { var h = OnSentence; if (h != null) h(s); };
                dev.OnMessageReceived += n => OnDeviceBeat?.Invoke(n);
                Devices[merged.Name] = dev;
            }
        }

        private static NmeaDeviceConfig Merge(NmeaDeviceConfig device, NmeaDriverConfig root)
        {
            device.Host = string.IsNullOrWhiteSpace(device.Host) ? null : device.Host;
            if (device.Port == 0) device.Port = 10110;
            if (device.ServiceIntervalMs == 0) device.ServiceIntervalMs = 1000;
            if (device.HeartbeatTimeoutMs == 0) device.HeartbeatTimeoutMs = 5000;
            device.Reconnect = device.Reconnect ?? root.DefaultReconnect ?? new ReconnectPolicy();
            device.Socket = device.Socket ?? root.DefaultSocket ?? new SocketSettings();
            return device;
        }

        public Task StartAllAsync(CancellationToken ct = default(CancellationToken))
        {
            var tasks = Devices.Values.Select(d => d.StartAsync(ct)).ToArray();
            return Task.WhenAll(tasks);
        }

        public Task StopAllAsync()
        {
            var tasks = Devices.Values.Select(d => d.StopAsync()).ToArray();
            return Task.WhenAll(tasks);
        }

        public void Dispose()
        {
            try { StopAllAsync().GetAwaiter().GetResult(); } catch { }
            foreach (var d in Devices.Values) { try { d.Dispose(); } catch { } }
            Devices.Clear();
        }

    }
}
