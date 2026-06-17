using System.Collections.Generic;

namespace ConnectML.UI.Models
{
    public class AppConfig
    {
        public string SourcePath { get; set; } = string.Empty;
        public bool IsBooleanMode { get; set; } = true;
        public string Protocol { get; set; } = "Siemens S7 (Profinet)";
        public bool AutoStartEnabled { get; set; } = false;
        public bool LastRunSuccessful { get; set; } = false;
        
        // Siemens S7
        public string IpAddress { get; set; } = "192.168.0.1";
        public string Rack { get; set; } = "0";
        public string Slot { get; set; } = "1";
        public string S7CpuType { get; set; } = "S71500";
        public string DbAddressBool { get; set; } = "DB10.0";
        public string DbAddressInt { get; set; } = "DB10.2";
        public string DbAddressPartNumber { get; set; } = "DB10.6";
        public string DbAddressStatus { get; set; } = "DB10.4";

        // Inbound / Local
        public int InboundPort { get; set; } = 5000;
        public string VirtualComPort { get; set; } = "COM3";

        // Webhook REST Genérico
        public string WebhookUrl { get; set; } = "http://localhost/api/results";
        public string WebhookVerb { get; set; } = "POST";
        public string AuthType { get; set; } = "None";
        public string AuthToken { get; set; } = "";
        public string HmacHeaderName { get; set; } = "X-Hub-Signature-256";
        public List<Models.CustomHeader> CustomHeaders { get; set; } = new List<Models.CustomHeader>();
        public string PayloadTemplate { get; set; } = "{\n  \"status\": \"{{Status}}\",\n  \"routine\": \"{{Routine}}\",\n  \"part\": \"{{Run}}\"\n}";
        public List<string> ConfigFields { get; set; } = new List<string> { "Boolean" };
    }
}
