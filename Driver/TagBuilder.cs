using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NMEA_FPU_DRIVER.Driver
{
    public sealed class TagBuilder
    {
        public static WriteValue CreateWriteValue<T>(string nodeId, T value, DateTime? timestamp = null, StatusCode? status = null)
        {
            DateTime time = timestamp ?? DateTime.UtcNow;
            var statusCode = status ?? new StatusCode(StatusCodes.Good);

            var dataValue = new DataValue(new Variant(value));
            dataValue.StatusCode = statusCode;
            dataValue.SourceTimestamp = time;

            var writeValue = new WriteValue
            {
                NodeId = NodeId.Parse(nodeId),
                AttributeId = Attributes.Value,
                Value = dataValue
            };

            return writeValue;
        }
    }
}
