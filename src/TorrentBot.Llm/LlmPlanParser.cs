using System.Text.Json;
using TorrentBot.Contracts.Llm;

namespace TorrentBot.Llm;

internal static class LlmPlanParser
{
    public static bool TryParse(string response, LlmPlanningRequest request, out PlanEnvelope plan)
    {
        plan = PlanEnvelopeFactory.Unsupported("empty");
        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        try
        {
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return false;
            }

            using var document = JsonDocument.Parse(response[start..(end + 1)]);
            var root = document.RootElement;
            if (!root.TryGetProperty("steps", out var stepsElement) || stepsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var allowed = request.Capabilities
                .Select(c => c.Name)
                .ToHashSet(StringComparer.Ordinal);

            var steps = new List<PlanStep>();
            foreach (var stepElement in stepsElement.EnumerateArray())
            {
                if (!stepElement.TryGetProperty("capability", out var capabilityElement))
                {
                    continue;
                }

                var capability = capabilityElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(capability) || !allowed.Contains(capability))
                {
                    continue;
                }

                Dictionary<string, object?>? parameters = null;
                if (stepElement.TryGetProperty("parameters", out var parametersElement)
                    && parametersElement.ValueKind == JsonValueKind.Object)
                {
                    parameters = JsonSerializer.Deserialize<Dictionary<string, object?>>(parametersElement.GetRawText());
                }

                var why = stepElement.TryGetProperty("why", out var whyElement)
                    ? whyElement.GetString()
                    : null;

                var condition = stepElement.TryGetProperty("condition", out var conditionElement)
                    ? conditionElement.GetString()
                    : null;
                var saveAs = stepElement.TryGetProperty("save_as", out var saveAsElement)
                    ? saveAsElement.GetString()
                    : null;
                steps.Add(new PlanStep(capability, parameters, why, Condition: condition, SaveAs: saveAs));
            }

            var intent = root.TryGetProperty("intent", out var intentElement)
                ? intentElement.GetString() ?? "planned"
                : "planned";
            var confidence = root.TryGetProperty("confidence", out var confidenceElement)
                && confidenceElement.TryGetDouble(out var parsedConfidence)
                ? parsedConfidence
                : 0.8;

            plan = new PlanEnvelope(intent, steps, confidence, Notes: "Parsed from LLM response");
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}