public class RecognitionResult
{
    public const string AlternativesKey = "alternatives";
    public const string ResultKey = "result";
    public const string PartialKey = "partial";

    public RecognizedPhrase[] Phrases;
    public bool Partial;

    public RecognitionResult(string json)
    {
        JSONObject resultJson = JSONNode.Parse(json).AsObject;

        if (resultJson.HasKey(AlternativesKey))
        {
            var alternatives = resultJson[AlternativesKey].AsArray;
            Phrases = new RecognizedPhrase[alternatives.Count];

            for (int i = 0; i < Phrases.Length; i++)
            {
                Phrases[i] = new RecognizedPhrase(alternatives[i].AsObject);
            }

        }
        else if (resultJson.HasKey(ResultKey))
        {
            Phrases = new RecognizedPhrase[] { new RecognizedPhrase(resultJson.AsObject) };
        }
        else if (resultJson.HasKey(PartialKey))
        {
            Partial = true;
            Phrases = new RecognizedPhrase[] { new RecognizedPhrase() { Text = resultJson[PartialKey] } };
        }
        else
        {
            Phrases = new[] { new RecognizedPhrase() { } };
        }
    }
}

public class RecognizedPhrase
{
    public const string ConfidenceKey = "confidence";
    public const string TextKey = "text";

    public string Text = "";
    public float Confidence = 0.0f;

    public RecognizedPhrase()
    {
    }

    public RecognizedPhrase(JSONObject json)
    {
        if (json.HasKey(ConfidenceKey))
        {
            Confidence = json[ConfidenceKey].AsFloat;
        }

        if (json.HasKey(TextKey))
        {
            //Vosk adds an extra space at the start of the string.
            Text = json[TextKey].Value.Trim();
        }
    }
}
