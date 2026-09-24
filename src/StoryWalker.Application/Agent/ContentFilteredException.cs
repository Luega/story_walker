namespace StoryWalker.Application.Agent;

/// <summary>
/// Thrown when the chat model's content filter refuses a turn, either the parent's message or the
/// model's reply (ADR-0024). The turn is rolled back, so the refused message never enters the stored
/// history: kept there, it would be resent, and refused again, on every later turn. The endpoint
/// maps this to 422 with a detail asking the parent to rephrase.
/// </summary>
public sealed class ContentFilteredException(Exception? innerException = null)
    : Exception("The model's content filter refused this turn.", innerException);
