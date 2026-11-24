namespace ChatServe.Api.Tests;

public static class SamplePayloads
{
    public static string TextMessage(string body, string messageId = "wamid.TEST1") => $$"""
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "id": "WABA_ID",
        "changes": [{
          "field": "messages",
          "value": {
            "messaging_product": "whatsapp",
            "metadata": { "display_phone_number": "15551234567", "phone_number_id": "111222333" },
            "contacts": [{ "profile": { "name": "Ali" }, "wa_id": "923001234567" }],
            "messages": [{
              "from": "923001234567",
              "id": "{{messageId}}",
              "timestamp": "1764000000",
              "type": "text",
              "text": { "body": "{{body}}" }
            }]
          }
        }]
      }]
    }
    """;

    public const string StatusUpdate = """
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "id": "WABA_ID",
        "changes": [{
          "field": "messages",
          "value": {
            "messaging_product": "whatsapp",
            "metadata": { "display_phone_number": "15551234567", "phone_number_id": "111222333" },
            "statuses": [{ "id": "wamid.OUT1", "status": "delivered", "timestamp": "1764000001", "recipient_id": "923001234567" }]
          }
        }]
      }]
    }
    """;
}
