using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ConnectML.Core.Interfaces;
using Serilog;

namespace ConnectML.UI.Endpoints
{
    public static class WebhookEndpoints
    {
        public static void MapWebhookEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/webhooks/incoming", async (HttpContext context, IInboundDispatcher dispatcher) =>
            {
                Log.Information("[Webhook Inbound] Requisição recebida em /api/webhooks/incoming.");
                
                using var reader = new StreamReader(context.Request.Body);
                string payload = await reader.ReadToEndAsync();
                
                if (string.IsNullOrWhiteSpace(payload))
                {
                    Log.Warning("[Webhook Inbound] Requisição ignorada: Payload vazio.");
                    return Results.BadRequest(new { error = "Payload vazio." });
                }

                Log.Information($"[Webhook Inbound] Payload recebido ({payload.Length} bytes). Despachando...");

                // Repassa assincronamente para a serial local
                await dispatcher.DispatchIncomingPayloadAsync(payload);
                
                return Results.Ok(new { message = "Comando processado e despachado para a serial local." });
            });
        }
    }
}
