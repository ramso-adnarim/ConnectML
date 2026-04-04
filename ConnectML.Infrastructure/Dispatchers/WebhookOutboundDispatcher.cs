using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using ConnectML.Core.Interfaces;
using ConnectML.Infrastructure.Formatters;

namespace ConnectML.Infrastructure.Dispatchers
{
    public class WebhookOutboundDispatcher : IOutboundDispatcher
    {
        // Singleton otimizado para evitar *socket exhaustion* em alta carga
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly LiquidPayloadFormatter _formatter;
        
        private readonly string _webhookUrl;
        private readonly string _webhookVerb;
        private readonly string _authType;
        private readonly string _authToken;
        private readonly string _payloadTemplate;
        private readonly IEnumerable<KeyValuePair<string, string>> _customHeaders;

        public WebhookOutboundDispatcher(
            string webhookUrl,
            string webhookVerb,
            string authType,
            string authToken,
            string payloadTemplate,
            IEnumerable<KeyValuePair<string, string>> customHeaders)
        {
            _webhookUrl = webhookUrl;
            _webhookVerb = webhookVerb;
            _authType = authType;
            _authToken = authToken;
            _payloadTemplate = payloadTemplate;
            _customHeaders = customHeaders;
            
            _formatter = new LiquidPayloadFormatter();
        }

        public async Task DispatchAsync(bool isOk, int failCount)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_webhookUrl))
                {
                    Log.Warning("[Webhook Outbound] Requisição abortada: URL de destino não configurada.");
                    return;
                }

                // 1. Processar o payload utilizando Liquid Tempaltes (Fluid)
                string finalPayload = _formatter.Format(_payloadTemplate, isOk, failCount);

                var method = _webhookVerb?.ToUpper() switch
                {
                    "PUT" => HttpMethod.Put,
                    "PATCH" => HttpMethod.Patch,
                    _ => HttpMethod.Post
                };

                using var request = new HttpRequestMessage(method, _webhookUrl);
                request.Content = new StringContent(finalPayload, Encoding.UTF8, "application/json");

                // 2. Anexar Custom Headers
                if (_customHeaders != null)
                {
                    foreach (var header in _customHeaders)
                    {
                        if (!string.IsNullOrWhiteSpace(header.Key))
                        {
                            // TryAddWithoutValidation permite aplicar cabeçalhos customizados pesados
                            request.Headers.TryAddWithoutValidation(header.Key, header.Value ?? string.Empty);
                        }
                    }
                }

                // 3. Aplicação de Autenticações Leves
                if (_authType == "Bearer" && !string.IsNullOrWhiteSpace(_authToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);
                }
                else if (_authType == "Basic" && !string.IsNullOrWhiteSpace(_authToken))
                {
                    // Consideramos que o customer inseriu "usuario:senha" na UI
                    var bytes = Encoding.UTF8.GetBytes(_authToken);
                    var encoded = Convert.ToBase64String(bytes);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
                }

                Log.Information($"[Webhook Outbound] Disparando {method.ToString()} para {_webhookUrl}...");

                // 4. Envio HTTP na nuvem
                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    Log.Information($"[Webhook Outbound] Resposta de sucesso recebida (StatusCode: {response.StatusCode}).");
                }
                else
                {
                    string errorMsg = await response.Content.ReadAsStringAsync();
                    Log.Warning($"[Webhook Outbound] O servidor respondeu com falha (StatusCode: {response.StatusCode}): {errorMsg}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Webhook Outbound] Erro crítico ao contatar serviço de destino: {ex.Message}");
            }
        }
    }
}
