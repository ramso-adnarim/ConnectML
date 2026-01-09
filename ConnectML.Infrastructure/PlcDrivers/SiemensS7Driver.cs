using ConnectML.Core.Interfaces;
using S7.Net;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ConnectML.Infrastructure.PlcDrivers
{
    /// <summary>
    /// Implementação do driver para CLPs Siemens (S7-1200, 1500, 300, 400) usando protocolo S7 (ISO-on-TCP).
    /// </summary>
    public class SiemensS7Driver : IPlcDriver
    {
        private Plc? _plc;
        private readonly ILogger<SiemensS7Driver> _logger;

        public string ProtocolName => "Siemens S7 (Profinet/ISO-on-TCP)";
        public bool IsConnected => _plc != null && _plc.IsConnected;

        public SiemensS7Driver(ILogger<SiemensS7Driver> logger)
        {
            _logger = logger;
        }

        public async Task ConnectAsync(string ip, int rack, int slot)
        {
            try
            {
                // Configuração padrão
                var cpuType = CpuType.S71500; // Compatível com S7-1500/1200

                _logger.LogInformation($"[S7 REAL] Tentando conectar ao CLP em {ip} (Rack: {rack}, Slot: {slot})...");

                // Inicializa a conexão
                _plc = new Plc(cpuType, ip, (short)rack, (short)slot);
                _plc.ReadTimeout = 3000;
                _plc.WriteTimeout = 3000;

                // Enforce connection timeout (S7NetPlus OpenAsync sometimes hangs indefinitely)
                var connectTask = _plc.OpenAsync();
                var timeoutTask = Task.Delay(5000); // 5 seconds hard timeout

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException("Timeout ao tentar conectar ao CLP (5s). Verifique se o IP está correto e o PLC acessível.");
                }

                // Ensure exception propagation if connectTask failed
                await connectTask;

                if (_plc.IsConnected)
                {
                    _logger.LogInformation("[S7 REAL] Conexão estabelecida com sucesso.");
                }
                else
                {
                    _logger.LogError("[S7 REAL] Falha ao abrir conexão com o CLP.");
                    throw new Exception("Falha ao abrir conexão com o CLP.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[S7 REAL] Erro crítico ao conectar: {ex.Message}");
                throw;
            }
        }

        public Task DisconnectAsync()
        {
            if (_plc != null)
            {
                _plc.Close();
                _plc = null;
                _logger.LogInformation("[S7 REAL] Desconectado.");
            }
            return Task.CompletedTask;
        }

        public async Task WriteBoolAsync(string dbAddress, bool value)
        {
            if (!IsConnected || _plc == null) throw new InvalidOperationException("CLP não conectado.");

            try
            {
                string finalAddress = NormalizeAddress(dbAddress, true);
                await _plc.WriteAsync(finalAddress, value);
                _logger.LogInformation($"[S7 REAL] Write Bit {finalAddress}: {value}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[S7 REAL] Erro ao escrever Bit em {dbAddress}.");
                throw;
            }
        }

        public async Task WriteIntAsync(string dbAddress, int value)
        {
            if (!IsConnected || _plc == null) throw new InvalidOperationException("CLP não conectado.");

            try
            {
                string finalAddress = NormalizeAddress(dbAddress, false);
                // Converte para short (Int16/WORD)
                await _plc.WriteAsync(finalAddress, (short)value);
                _logger.LogInformation($"[S7 REAL] Write Int {finalAddress}: {value}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[S7 REAL] Erro ao escrever Int em {dbAddress}.");
                throw;
            }
        }

        /// <summary>
        /// Normaliza endereços S7 abreviados ou incorretos para o formato esperado pelo S7NetPlus.
        /// Ex: "DB10.0" (Bool) -> "DB10.DBX0.0"
        /// Ex: "DB10.0" (Int)  -> "DB10.DBW0"
        /// </summary>
        private string NormalizeAddress(string address, bool isBool)
        {
            if (string.IsNullOrWhiteSpace(address)) return address;
            address = address.ToUpper().Trim();

            // Se já está no formato completo (contém DBX, DBW, DBD, etc), retorna.
            if (address.Contains("DBX") || address.Contains("DBW") || address.Contains("DBD") || address.Contains("DBB"))
                return address;

            // Tenta detectar formato curto "DBx.y" ou "DBx.y.z"
            // Ex: DB10.0
            if (address.StartsWith("DB"))
            {
                var parts = address.Substring(2).Split('.');
                if (parts.Length >= 2)
                {
                    string dbNum = parts[0];
                    string byteOffset = parts[1];
                    
                    if (isBool)
                    {
                        // Para Bool, precisamos de Bit Offset.
                        // Se veio DB10.0, assumimos DB10.DBX0.0
                        // Se veio DB10.0.1, assumimos DB10.DBX0.1
                        string bitOffset = parts.Length > 2 ? parts[2] : "0";
                        return $"DB{dbNum}.DBX{byteOffset}.{bitOffset}";
                    }
                    else
                    {
                        // Para Int (Word), usamos DBW
                        return $"DB{dbNum}.DBW{byteOffset}";
                    }
                }
            }

            return address;
        }
    }
}
