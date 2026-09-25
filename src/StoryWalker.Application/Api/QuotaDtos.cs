namespace StoryWalker.Application.Api;

/// <summary>
/// The caller's weekly story limit (ADR-0030), for <c>GET /me/quota</c>.
/// </summary>
/// <param name="Limit">Stories the user's plan allows per calendar week.</param>
/// <param name="Used">Finished this week, plus those still generating, which hold a slot.</param>
/// <param name="Remaining">What is left, never below zero.</param>
/// <param name="ResetsAt">The next Monday 00:00 UTC, when the week starts over.</param>
public record QuotaDto(int Limit, int Used, int Remaining, DateTime ResetsAt);
