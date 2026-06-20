namespace CaveAiProForWindows.Models;

public enum CaveAiAssistantReplySource
{
    Device,
    Cloud,
}

public sealed class CaveAiChatMessage
{
    public string Text { get; set; } = "";
    public bool IsUser { get; set; }
    public CaveAiAssistantReplySource ReplySource { get; set; }

    public string SourceBadge =>
        IsUser ? "" : ReplySource == CaveAiAssistantReplySource.Cloud ? "Cloud" : "On-device";
}