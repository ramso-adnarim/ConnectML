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

                // Encontra todos os elementos MeasurementResults independentemente do namespace
                var measurementResultsElements = doc.Descendants()
                    .Where(x => x.Name.LocalName == "MeasurementResults")
                    .ToList();

                if (!measurementResultsElements.Any())
                {
                    Log.Warning($"[QIF PARSER] Nenhum elemento MeasurementResults encontrado em {Path.GetFileName(filePath)}");
                    return (false, 0);
                }

                // Seleciona o MeasurementResults com o maior ID numérico
                var latestMeasurement = measurementResultsElements
                    .OrderByDescending(x =>
                    {
                        // Tenta parsear o atributo "id" como long
                        if (long.TryParse(x.Attribute("id")?.Value, out long id))
                            return id;
                        return -1; // Se falhar ou não tiver ID, joga pro fim (menor prioridade)
                    })
                    .First();

                var latestId = latestMeasurement.Attribute("id")?.Value;
                Log.Information($"[QIF PARSER] Processando a medição mais recente (ID: {latestId}) de {Path.GetFileName(filePath)}");

                // Encontra os elementos CharacteristicStatusEnum APENAS dentro da medição mais recente
                var statusElements = latestMeasurement.Descendants()
                    .Where(x => x.Name.LocalName == "CharacteristicStatusEnum")
                    .ToList();

                // Verifica se encontrou medições
                if (!statusElements.Any())
                {
                    Log.Warning($"[QIF PARSER] Nenhuma característica encontrada na medição ID {latestId}");
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

                // Verificar InspectionStatusEnum global para logging (da medição atual)
                var inspectionStatus = latestMeasurement.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "InspectionStatusEnum")?.Value;

                Log.Information($"[QIF PARSER] Resultado ID {latestId}: {inspectionStatus ?? "N/A"}. Falhas: {failCount}");

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
