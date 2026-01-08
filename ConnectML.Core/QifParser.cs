using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Serilog;

namespace ConnectML.Core
{
    public static class QifParser
    {
        public static (bool IsOk, int FailCount) Parse(string filePath)
        {
            try
            {
                // Carrega o documento XML
                var doc = XDocument.Load(filePath);

                // Encontra todos os elementos CharacteristicStatusEnum independentemente do namespace
                var statusElements = doc.Descendants()
                    .Where(x => x.Name.LocalName == "CharacteristicStatusEnum")
                    .ToList();

                // Verifica se encontrou medições
                if (!statusElements.Any())
                {
                    Log.Warning($"[QIF PARSER] Nenhuma medição encontrada em {Path.GetFileName(filePath)}");
                    // Se não tem medições, consideramos falha ou passamos 0 falhas?
                    // Vamos considerar que se não tem status, algo está errado, mas retornamos 0 falhas e IsOk=false?
                    // Ou melhor, retornamos falha na validação.
                    // Para simplificar, assumimos que se não tem medições, não é aprovado.
                    return (false, 0);
                }

                int failCount = 0;
                foreach (var status in statusElements)
                {
                    // Se o valor for diferente de "PASS", conta como falha
                    if (status.Value != "PASS")
                    {
                        failCount++;
                    }
                }

                bool isOk = failCount == 0;

                // Opcional: Verificar InspectionStatusEnum global para logging
                var inspectionStatus = doc.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "InspectionStatusEnum")?.Value;

                Log.Information($"[QIF PARSER] Arquivo processado: {Path.GetFileName(filePath)}. Status Global: {inspectionStatus ?? "N/A"}. Falhas: {failCount}");

                return (isOk, failCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[QIF PARSER] Erro ao processar arquivo {filePath}");
                throw;
            }
        }
    }
}
