using System;
using System.Threading.Tasks;

namespace ConnectML.Core.Interfaces
{
    /// <summary>
    /// Define o contrato genérico para qualquer comunicação com CLPs (S7, OPC UA, Modbus, etc).
    /// </summary>
    public interface IPlcDriver
    {
        /// <summary>
        /// Nome amigável do protocolo (ex: "Siemens S7", "OPC UA").
        /// </summary>
        string ProtocolName { get; }

        /// <summary>
        /// Indica se o driver está conectado ao dispositivo.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Tenta estabelecer conexão com o dispositivo baseado nas configurações fornecidas.
        /// </summary>
        /// <param name="connectionString">String de conexão ou IP.</param>
        /// <param name="config">Objeto de configuração flexível (rack, slot, porta, etc).</param>
        Task<bool> ConnectAsync(string connectionString, object? config = null);

        /// <summary>
        /// Fecha a conexão.
        /// </summary>
        void Disconnect();

        /// <summary>
        /// Escreve o status da inspeção (Aprovado/Reprovado).
        /// Geralmente mapeado para um Bit/Bool no CLP.
        /// </summary>
        /// <param name="address">Endereço da memória (ex: "DB10.DBX0.0").</param>
        /// <param name="isPassed">True para Aprovado, False para Reprovado.</param>
        Task WriteInspectionStatusAsync(string address, bool isPassed);

        /// <summary>
        /// Escreve a contagem de falhas (para modo Inteiro).
        /// </summary>
        /// <param name="address">Endereço da memória (ex: "DB10.DBW2").</param>
        /// <param name="failureCount">Número de características reprovadas.</param>
        Task WriteFailureCountAsync(string address, int failureCount);

        /// <summary>
        /// Envia um sinal de "Heartbeat" (Estou vivo) para o CLP monitorar o software.
        /// </summary>
        Task SendHeartbeatAsync(string address);
    }
}