using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Polly;
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
        private readonly string _hmacHeaderName;
        private readonly string _payloadTemplate;
        private readonly IEnumerable<KeyValuePair<string, string>> _customHeaders;

        public WebhookOutboundDispatcher(
            string webhookUrl,
            string webhookVerb,
            string authType,
            string authToken,
            string hmacHeaderName,
            string payloadTemplate,
            IEnumerable<KeyValuePair<string, string>> customHeaders)
        {
            _webhookUrl = webhookUrl;
            _webhookVerb = webhookVerb;
            _authType = authType;
            _authToken = authToken;
            _hmacHeaderName = string.IsNullOrWhiteSpace(hmacHeaderName) ? "X-Hub-Signature-256" : hmacHeaderName;
            _payloadTemplate = payloadTemplate;
            _customHeaders = customHeaders;
            
            _formatter = new LiquidPayloadFormatter();
        }

        public async Task DispatchAsync(bool isOk, int failCount, string product)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_webhookUrl))
                {
                    Log.Warning("[Webhook Outbound] Requisição abortada: URL de destino não configurada.");
                    return;
                }

                // 1. Processar o payload utilizando Liquid Tempaltes (Fluid)
                string finalPayload = _formatter.Format(_payloadTemplate, isOk, failCount, product);

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
                else if (_authType == "HMAC (Secret)" && !string.IsNullOrWhiteSpace(_authToken))
                {
                    var keyBytes = Encoding.UTF8.GetBytes(_authToken);
                    var payloadBytes = Encoding.UTF8.GetBytes(finalPayload);
                    using var hmac = new HMACSHA256(keyBytes);
                    var hashBytes = hmac.ComputeHash(payloadBytes);
                    var hashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                    request.Headers.TryAddWithoutValidation(_hmacHeaderName, "sha256=" + hashHex);
                }

                Log.Information($"[Webhook Outbound] Disparando {method.ToString()} para {_webhookUrl}...");

                // 4. Envio HTTP na nuvem com Polly (Exponential Backoff de 3 tentativas: 2s, 4s, 8s)
                var retryPolicy = Policy
                    .Handle<HttpRequestException>() // Falhas de rota e DNS
                    .OrResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500 || r.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
                    .WaitAndRetryAsync(
                        retryCount: 3,
                        sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // 2s, 4s, 8s
                        onRetry: (outcome, timespan, retryAttempt, context) =>
                        {
                            Log.Warning($"[Webhook Outbound] Falha no disparo. Tentativa {retryAttempt} de 3. Aguardando {timespan.TotalSeconds} segundos para re-tentativa...");
                        }
                    );

                var response = await retryPolicy.ExecuteAsync(async () =>
                {
                    // Como a policy pode tentar novamente e o HttpRequestMessage só pode ser enviado uma vez por instância,
                    // devemos fabricar aqui dentro uma cópia limpa do Request para a próxima tentativa se for falha.
                    using var retryRequest = new HttpRequestMessage(method, _webhookUrl);
                    retryRequest.Content = new StringContent(finalPayload, Encoding.UTF8, "application/json");

                    if (_customHeaders != null)
                    {
                        foreach (var header in _customHeaders)
                        {
                            if (!string.IsNullOrWhiteSpace(header.Key))
                                retryRequest.Headers.TryAddWithoutValidation(header.Key, header.Value ?? string.Empty);
                        }
                    }

                    if (request.Headers.Authorization != null)
                    {
                        retryRequest.Headers.Authorization = request.Headers.Authorization;
                    }
                    else if (request.Headers.Contains(_hmacHeaderName))
                    {
                        var hmacVal = request.Headers.GetValues(_hmacHeaderName);
                        retryRequest.Headers.TryAddWithoutValidation(_hmacHeaderName, hmacVal);
                    }

                    return await _httpClient.SendAsync(retryRequest);
                });

                if (response.IsSuccessStatusCode)
                {
                    Log.Information($"[Webhook Outbound] Resposta de sucesso recebida (StatusCode: {response.StatusCode}).");
                }
                else
                {
                    string errorMsg = await response.Content.ReadAsStringAsync();
                    Log.Error($"[Webhook Outbound] Falha definitiva no envio do Webhook após 3 tentativas. (StatusCode: {response.StatusCode}): {errorMsg}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Webhook Outbound] Falha definitiva no envio do Webhook após 3 tentativas. Erro crítico: {ex.Message}");
            }
        }
    }
}
