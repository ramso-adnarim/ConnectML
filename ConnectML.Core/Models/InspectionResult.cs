using System;
using System.Collections.Generic;
using System.Linq;

namespace ConnectML.Core.Models
{
    /// <summary>
    /// Representa o resultado processado de uma leitura de arquivo QIF.
    /// </summary>
    public class InspectionResult
    {
        public string PartName { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// Lista simples dos status de cada característica lida.
        /// True = PASS, False = FAIL.
        /// </summary>
        public List<bool> CharacteristicStatuses { get; set; } = new List<bool>();

        /// <summary>
        /// Contagem total de falhas.
        /// </summary>
        public int FailureCount => CharacteristicStatuses.Count(c => !c);

        /// <summary>
        /// Status global da peça (Se todas passaram).
        /// </summary>
        public bool IsGlobalPass => FailureCount == 0;

        /// <summary>
        /// Se houver falha, retorna false. (Modo "Any Failure").
        /// </summary>
        public bool IsAnyFailure => FailureCount > 0;
    }
}