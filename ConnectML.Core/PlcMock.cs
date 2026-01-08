using System.Threading.Tasks;
using ConnectML.Core.Interfaces;
using Serilog;

namespace ConnectML.Core
{
    public class MockPlcDriver : IPlcDriver
    {
        public bool IsConnected { get; private set; }

        public async Task ConnectAsync(string ip, int rack, int slot)
        {
            await Task.Delay(100); // Simula conexão
            IsConnected = true;
            Log.Information($"[PLC MOCK] Conectado ao PLC Siemens S7 em {ip} (Rack: {rack}, Slot: {slot})");
        }

        public async Task DisconnectAsync()
        {
            await Task.Delay(50);
            IsConnected = false;
            Log.Information("[PLC MOCK] Desconectado.");
        }

        public async Task WriteBoolAsync(string dbAddress, bool value)
        {
            if (!IsConnected)
            {
                Log.Warning("[PLC MOCK] Tentativa de escrita sem conexão!");
                return;
            }
            await Task.Delay(50);
            Log.Information($"[PLC MOCK] Escrevendo BOOL em {dbAddress} -> {value}");
        }

        public async Task WriteIntAsync(string dbAddress, int value)
        {
             if (!IsConnected)
            {
                Log.Warning("[PLC MOCK] Tentativa de escrita sem conexão!");
                return;
            }
            await Task.Delay(50);
            Log.Information($"[PLC MOCK] Escrevendo INT em {dbAddress} -> {value}");
        }
    }
}
