namespace CaveAiProForWindows.Services;

/// <summary>One .json file that was opened as survey data but failed to deserialize (not Cave Library).</summary>
public sealed class JsonSurveyLoadFailure
{
    public JsonSurveyLoadFailure(string filePath, string fileName, string message)
    {
        FilePath = filePath;
        FileName = fileName;
        Message = message;
    }

    public string FilePath { get; }
    public string FileName { get; }
    public string Message { get; }
}
