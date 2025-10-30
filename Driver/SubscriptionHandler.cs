using Opc.Ua;
using Opc.Ua.Client;
using OpcUaLib;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NMEA_FPU_DRIVER.Driver
{
    public sealed class SubscriptionHandler : ISubscriptionHandler
    {

        private DataHandler _handler;

        public SubscriptionHandler() {  }
        public async Task HandleAsync(MonitoredItem item, MonitoredItemNotificationEventArgs e, CancellationToken ct = default)
        {
            if (e.NotificationValue is MonitoredItemNotification monitoredItem)
            {
                DataValue val = monitoredItem.Value;
                

                if (item.DisplayName.Contains("FPU_TIMESYNC_ACTIVE_INTERFACE"))
                {
                    _handler.ActiveInterface = (int)val.Value;
                }
            }

            await Task.CompletedTask;
        }

        public void SetHandler(DataHandler handler) { _handler = handler; }
    }
}
