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
        // Keep track of connection params for reconnection
        private string? _lastIp;
        private int _lastRack;
        private int _lastSlot;

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

                _lastIp = ip;
                _lastRack = rack;
                _lastSlot = slot;

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
            await ExecuteWithRetryAsync(async () =>
            {
                var finalAddress = NormalizeAddress(dbAddress, true);
                if (_plc != null)
                {
                    await _plc.WriteAsync(finalAddress, value);
                    _logger.LogInformation($"[S7 REAL] Write Bit {finalAddress}: {value}");
                }
            }, "WriteBool");
        }

        public async Task WriteIntAsync(string dbAddress, int value)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                var finalAddress = NormalizeAddress(dbAddress, false);
                if (_plc != null)
                {
                    // Converte para short (Int16/WORD)
                    await _plc.WriteAsync(finalAddress, (short)value);
                    _logger.LogInformation($"[S7 REAL] Write Int {finalAddress}: {value}");
                }
            }, "WriteInt");
        }

        public async Task<bool> ReadBoolAsync(string dbAddress)
        {
            return await ExecuteWithRetryAndResultAsync(async () =>
            {
                var finalAddress = NormalizeAddress(dbAddress, true);
                if (_plc != null)
                {
                    var result = await _plc.ReadAsync(finalAddress);
                    bool bResult = result is bool b ? b : false;
                    _logger.LogInformation($"[S7 REAL] Read Bit {finalAddress}: {bResult}");
                    return bResult;
                }
                return false;
            }, "ReadBool");
        }

        public async Task<int> ReadIntAsync(string dbAddress)
        {
            return await ExecuteWithRetryAndResultAsync(async () =>
            {
                var finalAddress = NormalizeAddress(dbAddress, false);
                if (_plc != null)
                {
                    var result = await _plc.ReadAsync(finalAddress);
                    int iResult = 0;
                    if (result is short s) iResult = s;
                    else if (result is ushort us) iResult = us;
                    _logger.LogInformation($"[S7 REAL] Read Int {finalAddress}: {iResult}");
                    return iResult;
                }
                return 0;
            }, "ReadInt");
        }

        public async Task WriteStringAsync(string dbAddress, string value)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                var (dbNum, startByte) = ParseDbAddress(dbAddress);
                if (_plc != null)
                {
                    int maxLen = 254; // Padrão Siemens para strings normais em DBs
                    
                    // Converte string para bytes ASCII
                    byte[] stringBytes = System.Text.Encoding.ASCII.GetBytes(value ?? string.Empty);
                    if (stringBytes.Length > maxLen)
                    {
                        Array.Resize(ref stringBytes, maxLen);
                    }

                    // Buffer: Byte 0 = Max Length, Byte 1 = Real Length, Bytes 2+ = ASCII Data
                    byte[] buffer = new byte[maxLen + 2];
                    buffer[0] = (byte)maxLen;
                    buffer[1] = (byte)stringBytes.Length;
                    Array.Copy(stringBytes, 0, buffer, 2, stringBytes.Length);

                    // Escreve os bytes na DB no offset especificado
                    await Task.Run(() => _plc.WriteBytes(DataType.DataBlock, dbNum, startByte, buffer));
                    _logger.LogInformation($"[S7 REAL] Write String {dbAddress}: \"{value}\" (Max: {maxLen}, Real: {stringBytes.Length})");
                }
            }, "WriteString");
        }

        public async Task<string> ReadStringAsync(string dbAddress)
        {
            return await ExecuteWithRetryAndResultAsync(async () =>
            {
                var (dbNum, startByte) = ParseDbAddress(dbAddress);
                if (_plc != null)
                {
                    // Lê o cabeçalho de 2 bytes para determinar os tamanhos
                    byte[] header = await Task.Run(() => _plc.ReadBytes(DataType.DataBlock, dbNum, startByte, 2));
                    int maxLen = header[0];
                    int actualLen = header[1];

                    if (actualLen > 0)
                    {
                        // Lê a quantidade real de caracteres armazenados
                        byte[] stringBytes = await Task.Run(() => _plc.ReadBytes(DataType.DataBlock, dbNum, startByte + 2, actualLen));
                        string result = System.Text.Encoding.ASCII.GetString(stringBytes);
                        _logger.LogInformation($"[S7 REAL] Read String {dbAddress}: \"{result}\"");
                        return result;
                    }
                }
                return string.Empty;
            }, "ReadString");
        }

        private (int dbNum, int startByte) ParseDbAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("O endereço do CLP não pode estar vazio.");

            address = address.ToUpper().Trim();

            // Formato esperado: DB10.20 ou DB10.DBB20 ou DB10.DBX20.0
            if (address.StartsWith("DB"))
            {
                var parts = address.Substring(2).Split('.');
                if (parts.Length >= 2)
                {
                    string dbNumStr = parts[0];
                    string offsetStr = parts[1];

                    // Remove caracteres comuns do offset nativo como DBB, DBW, DBX etc
                    offsetStr = System.Text.RegularExpressions.Regex.Replace(offsetStr, @"[A-Z]+", "");

                    if (int.TryParse(dbNumStr, out int dbNum) && int.TryParse(offsetStr, out int startByte))
                    {
                        return (dbNum, startByte);
                    }
                }
            }

            throw new ArgumentException($"Endereço de DB S7 inválido: {address}. Formato esperado: DBX.Y (Ex: DB10.20)");
        }

        private async Task ExecuteWithRetryAsync(Func<Task> action, string operationName)
        {
            // 1. Pre-Check
            if (!IsConnected)
            {
                _logger.LogWarning($"[S7 REAL] {operationName}: Detectado desconectado antes da operação. Tentando reconectar...");
                await AttemptReconnectAsync();
            }

            try
            {
                // Attempt 1
                await action();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"[S7 REAL] {operationName}: Falha na primeira tentativa. Tentando reconectar e reenviar...");
                
                try
                {
                    // Force Reconnect
                    await AttemptReconnectAsync();
                    
                    // Attempt 2 (Retry)
                    await action();
                    _logger.LogInformation($"[S7 REAL] {operationName}: Sucesso na segunda tentativa.");
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, $"[S7 REAL] {operationName}: Falha definitiva após retry.");
                    throw; // Throw original or new exception? Usually throwing the last one is better for context.
                }
            }
        }

        private async Task<T> ExecuteWithRetryAndResultAsync<T>(Func<Task<T>> action, string operationName)
        {
            if (!IsConnected)
            {
                _logger.LogWarning($"[S7 REAL] {operationName}: Detectado desconectado antes da operação. Tentando reconectar...");
                await AttemptReconnectAsync();
            }

            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"[S7 REAL] {operationName}: Falha na primeira tentativa. Tentando reconectar e reenviar...");
                try
                {
                    await AttemptReconnectAsync();
                    var result = await action();
                    _logger.LogInformation($"[S7 REAL] {operationName}: Sucesso na segunda tentativa.");
                    return result;
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, $"[S7 REAL] {operationName}: Falha definitiva após retry.");
                    throw;
                }
            }
        }

        private async Task AttemptReconnectAsync()
        {
             if (_plc == null && _lastIp != null)
             {
                 // Re-create instance if missing (unlikely if constructor called, but safe)
                 var cpuType = CpuType.S71500;
                 _plc = new Plc(cpuType, _lastIp, (short)_lastRack, (short)_lastSlot);
             }

             if (_plc != null)
             {
                 try
                 {
                     await _plc.OpenAsync();
                 }
                 catch (Exception ex)
                 {
                     _logger.LogError(ex, "[S7 REAL] Falha ao tentar reconectar.");
                     // Don't throw here, let the caller fail the action
                 }
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
