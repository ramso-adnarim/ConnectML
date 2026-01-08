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

                _logger.LogInformation($"[S7Driver] Tentando conectar ao CLP em {ip} (Rack: {rack}, Slot: {slot})...");

                // Inicializa a conexão
                _plc = new Plc(cpuType, ip, (short)rack, (short)slot);

                await _plc.OpenAsync();

                if (_plc.IsConnected)
                {
                    _logger.LogInformation("[S7Driver] Conexão estabelecida com sucesso.");
                }
                else
                {
                    _logger.LogError("[S7Driver] Falha ao abrir conexão com o CLP.");
                    throw new Exception("Falha ao abrir conexão com o CLP.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[S7Driver] Erro crítico ao conectar: {ex.Message}");
                throw;
            }
        }

        public Task DisconnectAsync()
        {
            if (_plc != null)
            {
                _plc.Close();
                _plc = null;
                _logger.LogInformation("[S7Driver] Desconectado.");
            }
            return Task.CompletedTask;
        }

        public async Task WriteBoolAsync(string dbAddress, bool value)
        {
            if (!IsConnected || _plc == null) throw new InvalidOperationException("CLP não conectado.");

            try
            {
                await _plc.WriteAsync(dbAddress, value);
                _logger.LogInformation($"[S7Driver] Write Bit {dbAddress}: {value}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[S7Driver] Erro ao escrever Bit em {dbAddress}.");
                throw;
            }
        }

        public async Task WriteIntAsync(string dbAddress, int value)
        {
            if (!IsConnected || _plc == null) throw new InvalidOperationException("CLP não conectado.");

            try
            {
                // Converte para short (Int16/WORD)
                await _plc.WriteAsync(dbAddress, (short)value);
                _logger.LogInformation($"[S7Driver] Write Int {dbAddress}: {value}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[S7Driver] Erro ao escrever Int em {dbAddress}.");
                throw;
            }
        }
    }
}
