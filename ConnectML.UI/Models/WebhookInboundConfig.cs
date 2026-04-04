namespace ConnectML.UI.Models
{
    public class WebhookInboundConfig
    {
        public string AuthType { get; set; } = string.Empty;
        public string AuthToken { get; set; } = string.Empty;
        public string HmacHeaderName { get; set; } = "X-Hub-Signature-256";

        public WebhookInboundConfig(string authType, string authToken, string hmacHeaderName)
        {
            AuthType = authType;
            AuthToken = authToken;
            HmacHeaderName = string.IsNullOrWhiteSpace(hmacHeaderName) ? "X-Hub-Signature-256" : hmacHeaderName;
        }
    }
}
