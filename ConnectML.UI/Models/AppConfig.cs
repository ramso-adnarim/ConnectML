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
        public bool WasServiceRunning { get; set; } = false;
        
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
        public bool IsLocked { get; set; } = false;

        // Widget Overlay HUD (v1.3.0)
        public int OverlayBorderThickness { get; set; } = 3; // 1 a 60 px (padrão: 3)
        public string OverlaySnapPosition { get; set; } = "Top";
        public int OverlayHoldSeconds { get; set; } = 10;
        public double OverlayFontSize { get; set; } = 13; // 11 a 60 pt (padrão: 13)

        // Leitor de Código de Barras (v1.3.1)
        public bool BarcodeReaderEnabled { get; set; } = false;
        public string BarcodeReaderPort { get; set; } = string.Empty;
        public string BarcodeOutputPort { get; set; } = string.Empty;
        public List<ConnectML.Core.Models.BarcodeRuleConfig> BarcodeRules { get; set; } = new List<ConnectML.Core.Models.BarcodeRuleConfig>();

        // Catálogo de Comandos customizável via appsettings.json
        public List<ConnectML.Core.Models.BarcodeCommandDefinition> BarcodeCommands { get; set; } = new List<ConnectML.Core.Models.BarcodeCommandDefinition>
        {
            new ConnectML.Core.Models.BarcodeCommandDefinition
            {
                Id = "undo",
                Name = "Desfazer (Alt + F + O)",
                KeySequence = "%{f}{o}",
                TargetWindowTitle = "MeasurLink",
                PreDelayMs = 150
            }
        };
    }
}
