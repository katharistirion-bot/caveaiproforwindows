namespace CaveAiProForWindows.Services.Auth;

/// <summary>Parsed <c>app_install_grace/{graceDocId}</c> document (server-anchored trial window).</summary>
public sealed record InstallGraceRecord(long FirstSeenMsUtc);
