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

        public async Task<bool> ConnectAsync(string ipAddress, object? config = null)
        {
            try
            {
                // Configuração padrão se não for fornecida (Rack 0, Slot 1 é comum para S7-1500/1200)
                var rack = 0;
                var slot = 1;
                var cpuType = CpuType.S71500; // Padrão moderno, mas compatível com S7-1200

                // CORREÇÃO: Removemos o padrão inválido "is dynamic"
                if (config != null)
                {
                    try
                    {
                        // Tratamos como dynamic de forma direta
                        dynamic dConfig = config;

                        // Tenta ler as propriedades (caso o objeto passado tenha Rack/Slot)
                        // Usamos int no cast para evitar exceções de unboxing, depois convertemos para short
                        rack = (int)dConfig.Rack;
                        slot = (int)dConfig.Slot;
                    }
                    catch
                    {
                        // Se falhar a leitura dinâmica ou as propriedades não existirem,
                        // mantém os valores padrão (0 e 1) e segue o jogo.
                        _logger.LogWarning("[S7Driver] Configuração dinâmica inválida ou ausente. Usando Rack 0, Slot 1.");
                    }
                }

                _logger.LogInformation($"[S7Driver] Tentando conectar ao CLP em {ipAddress} (Rack: {rack}, Slot: {slot})...");

                // Inicializa a conexão
                _plc = new Plc(cpuType, ipAddress, (short)rack, (short)slot);

                await _plc.OpenAsync();

                if (_plc.IsConnected)
                {
                    _logger.LogInformation("[S7Driver] Conexão estabelecida com sucesso.");
                    return true;
                }
                else
                {
                    _logger.LogError("[S7Driver] Falha ao abrir conexão com o CLP.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[S7Driver] Erro crítico ao conectar: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            if (_plc != null)
            {
                _plc.Close();
                _plc = null;
                _logger.LogInformation("[S7Driver] Desconectado.");
            }
        }

        public async Task WriteInspectionStatusAsync(string address, bool isPassed)
        {
            if (!IsConnected || _plc == null) throw new InvalidOperationException("CLP não conectado.");

            try
            {
                await _plc.WriteAsync(address, isPassed);
                _logger.LogInformation($"[S7Driver] Write Bit {address}: {isPassed}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[S7Driver] Erro ao escrever Bit em {address}.");
                throw;
            }
        }

        public async Task WriteFailureCountAsync(string address, int failureCount)
        {
            if (!IsConnected || _plc == null) throw new InvalidOperationException("CLP não conectado.");

            try
            {
                // Converte para short (Int16/WORD)
                await _plc.WriteAsync(address, (short)failureCount);
                _logger.LogInformation($"[S7Driver] Write Int {address}: {failureCount}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[S7Driver] Erro ao escrever Int em {address}.");
                throw;
            }
        }

        public async Task SendHeartbeatAsync(string address)
        {
            if (!IsConnected || _plc == null) return;

            try
            {
                // Escreve true apenas para manter link ativo
                await _plc.WriteAsync(address, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[S7Driver] Falha no Heartbeat: {ex.Message}");
            }
        }
    }
}