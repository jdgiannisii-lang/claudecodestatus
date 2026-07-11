namespace ClaudeTracker;

public readonly record struct OAuthCompletion(string Code, string? State)
{
    public bool HasCode => !string.IsNullOrWhiteSpace(Code);
}

public static class OAuthCompletionParser
{
    public static OAuthCompletion Parse(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        var hash = text.IndexOf('#');
        return hash < 0 ? new OAuthCompletion(text, null) : new OAuthCompletion(text[..hash].Trim(), text[(hash + 1)..].Trim());
    }

    public static bool MatchesPendingState(OAuthCompletion completion, string pendingState) =>
        completion.State == null || string.Equals(completion.State, pendingState, StringComparison.Ordinal);
}
