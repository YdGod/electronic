using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BatteryPet
{
    [DataContract]
    public sealed class DeviceBattery
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public string Kind;
        [DataMember] public string Connection;
        [DataMember] public int? Percent;
        [DataMember] public bool? Charging;
        [DataMember] public string Source;
        [DataMember] public string Note;
        [DataMember] public bool? Connected;
        [DataMember] public DateTime? ReadAt;
        public DeviceBattery() { Source = "Windows"; Connection = "USB / 接收器"; }
        public string StatusText
        {
            get
            {
                if (Connected == false) return Connection == "蓝牙" ? "已配对 · 未连接" : "设备未连接 / 休眠";
                if (!Percent.HasValue) return "暂未读取电量";
                if (Charging == true) return "正在充电";
                if (Charging == false) return "未充电";
                return "充电状态未知";
            }
        }
    }

    [DataContract]
    public sealed class ScanResult
    {
        [DataMember] public List<DeviceBattery> Devices = new List<DeviceBattery>();
        [DataMember] public DateTime ScannedAt = DateTime.Now;
        [DataMember] public List<string> Warnings = new List<string>();
        [DataMember] public ThermalSnapshot Temperatures;
    }
}
