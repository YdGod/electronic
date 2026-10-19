using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BatteryPet
{
    [DataContract] internal sealed class PnpNode
    {
        [DataMember] public string Id, Parent, Container, Name, Class;
        [DataMember] public int? Battery;
    }
    internal sealed class DeviceInterface
    {
        public string Path, InstanceId;
    }
}
