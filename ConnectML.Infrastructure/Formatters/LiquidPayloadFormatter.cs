using System;
using Fluid;
using Serilog;

namespace ConnectML.Infrastructure.Formatters
{
    public class LiquidPayloadFormatter
    {
        private readonly FluidParser _parser;

        public LiquidPayloadFormatter()
        {
            _parser = new FluidParser();
        }

        public string Format(string templateSource, bool isOk, int failCount, string product)
        {
            if (string.IsNullOrWhiteSpace(templateSource))
            {
                Log.Warning("[Liquid Formatter] O template de Payload está vazio. Retornando JSON padrão.");
                return $"{{\"IsOk\": {isOk.ToString().ToLower()}, \"FailCount\": {failCount}, \"Product\": \"{product}\"}}";
            }

            try
            {
                if (_parser.TryParse(templateSource, out IFluidTemplate template, out string error))
                {
                    var options = new TemplateOptions();

                    // Model anonimo ou tipado
                    var model = new { IsOk = isOk, FailCount = failCount, Product = product };

                    var context = new TemplateContext(model, options);
                    string result = template.Render(context);

                    return result;
                }
                else
                {
                    Log.Error($"[Liquid Formatter] Erro de sintaxe no template fornecido pela UI: {error}");
                    throw new Exception("Falha ao compilar Template Liquid: " + error);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Liquid Formatter] Falha crítica na formatação do payload: {ex.Message}");
                throw;
            }
        }
    }
}
