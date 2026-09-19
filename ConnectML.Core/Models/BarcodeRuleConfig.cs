using System;

namespace ConnectML.Core.Models
{
    /// <summary>
    /// Configuração de uma regra de intercepção de código de barras.
    /// Quando a leitura serial coincidir com Keyword, a aplicação não despacha para a COM de saída
    /// e aciona o comando especificado por CommandId.
    /// </summary>
    public class BarcodeRuleConfig
    {
        /// <summary>
        /// Palavra-chave ou código de barras exato a ser interceptado.
        /// </summary>
        public string Keyword { get; set; } = string.Empty;

        /// <summary>
        /// Identificador do comando configurado para execução (ex: "undo").
        /// </summary>
        public string CommandId { get; set; } = string.Empty;
    }
}
