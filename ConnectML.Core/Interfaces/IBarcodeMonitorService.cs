using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConnectML.Core.Models;

namespace ConnectML.Core.Interfaces
{
    /// <summary>
    /// Contrato de serviço para monitoramento serial e transporte de leituras do leitor de código de barras.
    /// Opera em segundo plano de forma 100% concorrente e desacoplada do fluxo principal.
    /// </summary>
    public interface IBarcodeMonitorService : IDisposable
    {
        /// <summary>
        /// Indica se o monitoramento serial está ativo no momento.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Inicia o monitoramento na porta COM do leitor e o transporte para a porta de saída.
        /// </summary>
        Task StartMonitoringAsync(string readerPort, string outputPort, IEnumerable<BarcodeRuleConfig> rules, CancellationToken cancellationToken = default);

        /// <summary>
        /// Interrompe o monitoramento serial e fecha as portas abertas.
        /// </summary>
        Task StopMonitoringAsync();

        /// <summary>
        /// Atualiza as regras ativas de intercepção sem reiniciar a conexão serial.
        /// </summary>
        void UpdateRules(IEnumerable<BarcodeRuleConfig> rules);

        /// <summary>
        /// Evento disparado sempre que uma leitura é processada (seja transportada ou interceptada).
        /// </summary>
        event EventHandler<BarcodeTransportResult>? ReadingProcessed;
    }
}
