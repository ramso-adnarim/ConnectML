using System;

namespace ConnectML.UI.Models
{
    public class AppConfig
    {
        public string SourcePath { get; set; } = string.Empty;
        public bool IsBooleanMode { get; set; } = true;
        public string Protocol { get; set; } = "Siemens S7 (Profinet)";
        public string IpAddress { get; set; } = "192.168.0.1";
        public string Rack { get; set; } = "0";
        public string Slot { get; set; } = "1";
        public string DbAddressBool { get; set; } = "DB10.0";
        public string DbAddressInt { get; set; } = "DB10.2";
    }
}
