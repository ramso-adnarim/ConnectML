using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Serilog;

namespace ConnectML.Core
{
    public static class QifParser
    {
        public static (bool IsOk, int FailCount, string Product, string SerialNumber) Parse(string filePath)
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
                    return (false, 0, "Desconhecido", string.Empty);
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
                    Log.Warning($"[QIF PARSER] Nenhuma característica encontrada em ID {latestId}");
                    return (false, 0, "Desconhecido", string.Empty);
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

                // Extrai nome do Produto/Rotina
                var productName = doc.Descendants()
                    .Where(x => x.Name.LocalName == "Product")
                    .Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "Name")?.Value ?? "Desconhecido";

                // Extrai o Número de Série (SerialNumber) correlacionado ao ActualComponent da medição mais recente
                string? serialNumber = null;
                var componentId = latestMeasurement.Descendants()
                    .Where(x => x.Name.LocalName == "ActualComponentIds")
                    .Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "Id")?.Value;

                if (!string.IsNullOrWhiteSpace(componentId))
                {
                    var matchingComponent = doc.Descendants()
                        .FirstOrDefault(x => x.Name.LocalName == "ActualComponent" && x.Attribute("id")?.Value == componentId);

                    serialNumber = matchingComponent?.Descendants()
                        .FirstOrDefault(x => x.Name.LocalName == "SerialNumber")?.Value?.Trim();
                }

                // Fallback resiliente 1: caso não encontre por ActualComponentIds, busca o ActualComponent com maior ID
                if (string.IsNullOrWhiteSpace(serialNumber))
                {
                    var allComponents = doc.Descendants()
                        .Where(x => x.Name.LocalName == "ActualComponent")
                        .ToList();

                    if (allComponents.Any())
                    {
                        var fallbackComponent = allComponents
                            .OrderByDescending(x =>
                            {
                                if (long.TryParse(x.Attribute("id")?.Value, out long id))
                                    return id;
                                return -1;
                            })
                            .FirstOrDefault();

                        serialNumber = fallbackComponent?.Descendants()
                            .FirstOrDefault(x => x.Name.LocalName == "SerialNumber")?.Value?.Trim();
                    }
                }

                // Fallback resiliente 2: qualquer tag SerialNumber no documento
                if (string.IsNullOrWhiteSpace(serialNumber))
                {
                    serialNumber = doc.Descendants()
                        .FirstOrDefault(x => x.Name.LocalName == "SerialNumber")?.Value?.Trim() ?? string.Empty;
                }
                else
                {
                    serialNumber ??= string.Empty;
                }

                Log.Information($"[QIF PARSER] Resultado ID {latestId} (Produto: {productName}, Serial: {(!string.IsNullOrEmpty(serialNumber) ? serialNumber : "N/A")}): {inspectionStatus ?? "N/A"}. Falhas: {failCount}");

                return (isOk, failCount, productName, serialNumber);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[QIF PARSER] Erro ao processar arquivo {filePath}");
                throw;
            }
        }
    }
}
