using System;

namespace ConnectML.Core.Models
{
    /// <summary>
    /// Resultado de uma leitura serial processada pelo monitor de código de barras.
    /// Informa se o dado foi transportado para a COM de saída ou se foi interceptado como comando.
    /// </summary>
    public class BarcodeTransportResult
    {
        /// <summary>
        /// Dado bruto lido da porta serial.
        /// </summary>
        public string RawData { get; set; } = string.Empty;

        /// <summary>
        /// Indica se a leitura coincidiu com uma palavra-chave cadastrada (interceptada).
        /// </summary>
        public bool IsIntercepted { get; set; }

        /// <summary>
        /// ID do comando acionado caso a leitura tenha sido interceptada.
        /// </summary>
        public string? ExecutedCommandId { get; set; }

        /// <summary>
        /// Nome amigável do comando executado.
        /// </summary>
        public string? ExecutedCommandName { get; set; }

        /// <summary>
        /// Veredito do transporte ou da execução do comando.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Mensagem de detalhe ou erro, se houver.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Momento em que a leitura foi processada.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
