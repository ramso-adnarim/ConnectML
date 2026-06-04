using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using ConnectML.Core.Interfaces;
using Serilog;
using ConnectML.UI.Models;

namespace ConnectML.UI.Endpoints
{
    public static class WebhookEndpoints
    {
        public static void MapWebhookEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/webhooks/incoming", async (HttpContext context, IInboundDispatcher dispatcher, WebhookInboundConfig config) =>
            {
                Log.Information("[WEBHOOK-IN] Requisição recebida em /api/webhooks/incoming.");
                
                // Allow reading body multiple times (just in case)
                context.Request.EnableBuffering();

                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                string payload = await reader.ReadToEndAsync();
                
                // Reset body stream position
                context.Request.Body.Position = 0;
                
                if (string.IsNullOrWhiteSpace(payload))
                {
                    Log.Warning("[WEBHOOK-IN] Requisição ignorada: Payload vazio.");
                    return Results.BadRequest(new { error = "Payload vazio." });
                }

                // Autenticação
                if (config.AuthType == "HMAC (Secret)")
                {
                    if (!context.Request.Headers.TryGetValue(config.HmacHeaderName, out var signatureHeader) || string.IsNullOrWhiteSpace(signatureHeader))
                    {
                        Log.Warning($"[WEBHOOK-IN] Falha: Cabeçalho HMAC '{config.HmacHeaderName}' ausente.");
                        return Results.Unauthorized();
                    }

                    try
                    {
                        var keyBytes = Encoding.UTF8.GetBytes(config.AuthToken);
                        var payloadBytes = Encoding.UTF8.GetBytes(payload);
                        using var hmac = new HMACSHA256(keyBytes);
                        var hashBytes = hmac.ComputeHash(payloadBytes);
                        var hashHex = "sha256=" + System.BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                        if (!string.Equals(hashHex, signatureHeader.ToString(), System.StringComparison.OrdinalIgnoreCase))
                        {
                            Log.Warning("[WEBHOOK-IN] Tentativa de injeção bloqueada: Assinatura HMAC inválida.");
                            return Results.Unauthorized();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error($"[WEBHOOK-IN] Falha ao processar HMAC: {ex.Message}");
                        return Results.Unauthorized();
                    }
                }

                Log.Information($"[WEBHOOK-IN] Payload recebido ({payload.Length} bytes). Despachando...");

                // Repassa assincronamente para a serial local
                await dispatcher.DispatchIncomingPayloadAsync(payload);
                
                return Results.Ok(new { message = "Comando processado e despachado para a serial local." });
            });
        }
    }
}
