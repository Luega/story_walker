namespace StoryWalker.Application.Agent;

/// <summary>One page of the story a new conversation is based on (ADR-0029).</summary>
public record PreviousStoryPage(int PageNo, string Text, string IllustrationPrompt);
