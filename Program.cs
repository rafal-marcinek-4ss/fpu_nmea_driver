using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using NMEA_FPU_DRIVER.Config;
using NMEA_FPU_DRIVER.Driver;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Topshelf;

namespace NMEA_FPU_DRIVER
{
    internal class Program
    {
        static int Main(string[] args)
        {
            return (int)HostFactory.Run(cfg =>
            {
                cfg.UseNLog();

                cfg.Service<DriverRunner>(s =>
                {
                    s.ConstructUsing(() => new DriverRunner());
                    s.WhenStarted(r => r.Start());
                    s.WhenStopped(r => r.Stop());
                });

                cfg.SetServiceName("RMS_FPU_DRIVER");
                cfg.SetDisplayName("RMS FPU DRIVER");
                cfg.SetDescription("App for bridging NMEA FPU data to OPC UA SERVER");
                cfg.RunAsLocalSystem();
                cfg.StartAutomatically();

                cfg.EnableServiceRecovery(r =>
                {
                    r.RestartService(1);
                    r.SetResetPeriod(1);
                });
            });
        }

        public class DriverRunner
        {
            private readonly ILoggerFactory loggerFactory;
            private readonly ILogger<DriverRunner> _log;

            private CancellationTokenSource _cts;
            private Task _runTask;

            private UdpDriver _driver;
            private DataHandler _handler;
            public DriverRunner()
            {
                //Logger factory for loggers
                loggerFactory = LoggerFactory.Create(b =>
                {
                    b.ClearProviders();
                    b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
                    b.AddNLog("NLOG.config");
                });
                _log = loggerFactory.CreateLogger<DriverRunner>();
            }


            public void Start()
            {
                if (_runTask != null) return;
                _cts = new CancellationTokenSource();
                _runTask = Task.Run(() => RunAsync(_cts.Token));
            }


            public void Stop()
            {

            }

            private async Task RunAsync(CancellationToken token)
            {
                _log.LogInformation("Service starting...");

                NmeaDriverConfig cfg;
                //OpcConfig opcCfg;

                try
                {
                    var json = File.ReadAllText("config.json");
                    cfg = NmeaDriverConfig.FromJson(json);
                    
                    //opcCfg = OpcUaLib.ConfigLoader.LoadOpcConfig();
                }
                catch (Exception ex) { _log.LogError($"ERROR LOADING CONFIG FILES: {ex.Message}"); throw; }



                var devices = FpuFactory.CreateDevice(cfg);


                _driver = new UdpDriver(cfg);
                
                _driver.OnDeviceStatusChanged += (status, name) =>
                    _log.LogInformation($"DEVICE: {name} -> {status}");



                _driver.StartAllAsync(token).GetAwaiter().GetResult();
                _log.LogInformation($"Service started!");

                _handler = new DataHandler(_driver, h => _driver.OnSentence += h, devices, cfg);


                try { await Task.Delay(Timeout.Infinite, token); } catch { }
                _driver.StopAllAsync().GetAwaiter().GetResult();

            }
        }
    }
}
