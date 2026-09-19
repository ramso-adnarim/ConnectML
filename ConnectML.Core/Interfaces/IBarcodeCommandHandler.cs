using System.Collections.Generic;
using System.Threading.Tasks;
using ConnectML.Core.Models;

namespace ConnectML.Core.Interfaces
{
    /// <summary>
    /// Contrato para execução de comandos acionados por leituras de código de barras.
    /// Permite focar a aplicação-alvo e simular atalhos de teclado.
    /// </summary>
    public interface IBarcodeCommandHandler
    {
        /// <summary>
        /// Executa o comando identificado por commandId associado à palavra-chave.
        /// </summary>
        Task<bool> ExecuteCommandAsync(string commandId, string keyword);

        /// <summary>
        /// Obtém o catálogo de comandos disponíveis (carregados da configuração).
        /// </summary>
        IReadOnlyList<BarcodeCommandDefinition> GetAvailableCommands();

        /// <summary>
        /// Recarrega ou atualiza a lista de comandos em tempo de execução.
        /// </summary>
        void ReloadCommands(IEnumerable<BarcodeCommandDefinition> commands);
    }
}
