using System;

namespace ConnectML.Core.Models
{
    /// <summary>
    /// Definição técnica de um comando acionado pelo leitor de código de barras.
    /// Permite customização e extensão direta via JSON (appsettings.json).
    /// </summary>
    public class BarcodeCommandDefinition
    {
        /// <summary>
        /// Identificador único do comando (ex: "undo", "confirm", "next_piece").
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Nome amigável exibido nas ComboBoxes da UI (ex: "Desfazer (Alt + Q + O)").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Sequência de teclas a ser simulada (padrão SendKeys, ex: "%{q}{o}" ou "%qo" para Alt+Q+O, "{ENTER}", etc.).
        /// </summary>
        public string KeySequence { get; set; } = string.Empty;

        /// <summary>
        /// Título ou correspondência parcial da janela-alvo no Windows (padrão: "MeasurLink").
        /// </summary>
        public string TargetWindowTitle { get; set; } = "MeasurLink";

        /// <summary>
        /// Atraso em milissegundos após trazer a janela para o foco antes de despachar as teclas.
        /// Garante que o Windows estabilizou o foco de entrada de teclado. Padrão: 80ms.
        /// </summary>
        public int PreDelayMs { get; set; } = 80;
    }
}
