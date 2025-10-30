using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;


namespace NMEA_FPU_DRIVER.Config
{
    public sealed class NmeaDriverConfig
    {
        [JsonProperty("devices")] public List<NmeaDeviceConfig> Devices { get; set; }
        [JsonProperty("nmea")] public NmeaSettings Nmea { get; set; }
        [JsonProperty("reconnect")] public ReconnectPolicy DefaultReconnect { get; set; }
        [JsonProperty("socket")] public SocketSettings DefaultSocket { get; set; }
        [JsonProperty("subscriptionTags")] public List<SubscriptionTags> SubscriptionTags { get; set; }
        [JsonProperty("writeTags")] public List<WriteTag> WriteTags { get; set; }
        [JsonProperty("dataHandler")] public DataHandlerSettings DataHandler { get; set; }

        public NmeaDriverConfig()
        {
            Devices = new List<NmeaDeviceConfig>();
            Nmea = new NmeaSettings();
            DefaultReconnect = new ReconnectPolicy();
            DefaultSocket = new SocketSettings();
        }


        public static NmeaDriverConfig FromJson(string json)
        {
            var cfg = JsonConvert.DeserializeObject<NmeaDriverConfig>(json);
            if (cfg == null) throw new InvalidOperationException("Config deserialization failed");
            return cfg;
        }

        public static NmeaDriverConfig FromFile(string path)
        {
            return FromJson(File.ReadAllText(path));
        }

        public static T Clone<T>(T obj)
            => obj == null ? default(T) : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(obj));
    }

    public sealed class NmeaDeviceConfig
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("host")] public string Host { get; set; }
        [JsonProperty("logicalName")] public string LogicalName { get; set; }
        [JsonProperty("port")] public int Port { get; set; }
        [JsonProperty("serviceIntervalMs")] public int ServiceIntervalMs { get; set; }
        [JsonProperty("heartbeatTimeoutMs")] public int HeartbeatTimeoutMs { get; set; }
        [JsonProperty("reconnect")] public ReconnectPolicy Reconnect { get; set; }
        [JsonProperty("socket")] public SocketSettings Socket { get; set; }

        public NmeaDeviceConfig()
        {
            Name = "";
            Host = "";
            Port = 10110;
            ServiceIntervalMs = 1000;
            HeartbeatTimeoutMs = 5000;
            Reconnect = new ReconnectPolicy();
            Socket = new SocketSettings();
        }
    }

    public sealed class ReconnectPolicy
    {
        [JsonProperty("initialDelayMs")] public int InitialDelayMs { get; set; }
        [JsonProperty("maxDelayMs")] public int MaxDelayMs { get; set; }
        [JsonProperty("multiplier")] public double Multiplier { get; set; }
        [JsonProperty("jitterMs")] public int JitterMs { get; set; }

        public ReconnectPolicy()
        {
            InitialDelayMs = 1000;
            MaxDelayMs = 30000;
            Multiplier = 2.0;
            JitterMs = 250;
        }
    }

    public sealed class DataHandlerSettings
    {
        [JsonProperty("tickIintervalMs")] public int TickIntervalMs { get; set; }
        [JsonProperty("initialDelayMs")] public int InitialDelayMs { get; set; }
        [JsonProperty("timeSyncPeriodMinutes")] public int TimeSyncPeriodMinutes { get; set; }

        public DataHandlerSettings()
        {
            TickIntervalMs = 1000;
            InitialDelayMs = 5000;
            TimeSyncPeriodMinutes = 1;
        }
    }

    public sealed class SocketSettings
    {
        [JsonProperty("receiveTimeoutMs")] public int ReceiveTimeoutMs { get; set; }
        [JsonProperty("SendTimeoutMs")] public int SendTimeoutMs { get; set; }
        [JsonProperty("keepAlive")] public bool KeepAlive { get; set; }
        [JsonProperty("bufferSize")] public int BufferSize { get; set; }
        [JsonProperty("localAddress")] public string LocalAddress { get; set; }
        [JsonProperty("multicastGroup")] public string MulticastGroup { get; set; }
        public SocketSettings()
        {
            ReceiveTimeoutMs = 3000;
            SendTimeoutMs = 3000;
            KeepAlive = true;
            BufferSize = 8 * 1024;
            LocalAddress = null;
            MulticastGroup = null;
        }
    }

    public sealed class NmeaSettings
    {
        [JsonProperty("emitInvalid")] public bool EmitInvalid { get; set; }
        public NmeaSettings() { EmitInvalid = false; }
    }

    public sealed class SubscriptionTags
    {
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("quantity")] public int Quantity { get; set; }
        [JsonProperty("replaceKeyword")] public string ReplaceKeyword { get; set; }
    }

    public sealed class WriteTag
    {
        [JsonProperty("name")] public string Name { get; set; } 
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("quantity")] public int Quantity { get; set; }
        [JsonProperty("replaceKeyword")] public string ReplaceKeyword { get; set; }
        [JsonProperty("dataType")][JsonConverter(typeof(StringEnumConverter))] public DataType DataType { get; set; }

    }

    public enum DataType
    {
        String,
        Word,       
        Int,
        Long,
        Float,
        Double,
        Decimal,
        Bool,
        DateTime,
        Bytes
    }

}
