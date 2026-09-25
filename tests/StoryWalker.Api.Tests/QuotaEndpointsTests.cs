using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoryWalker.Application.Api;
using StoryWalker.Application.Quota;
using StoryWalker.Domain.Entities;

namespace StoryWalker.Api.Tests;

/// <summary>
/// The weekly story limit over HTTP (ADR-0030): what <c>GET /me/quota</c> reports, and the chat
/// refusing a turn before the model is called once the limit is reached. The API runs on the real
/// clock, so ledger rows are dated a minute ago: always inside the current week, except in the
/// first minute of a Monday.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class QuotaEndpointsTests(StoryWalkerApiFactory factory)
{
    private const string Me = "user_quota_endpoint_tests";

    private readonly StoryWalkerApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClientAs(Me);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Quota_reports_what_is_used_left_and_when_the_week_resets()
    {
        await ResetWithFinishedStoriesAsync(2);

        var quota = await _client.GetFromJsonAsync<QuotaDto>("/me/quota", Ct);

        Assert.NotNull(quota);
        Assert.Equal(7, quota.Limit);
        Assert.Equal(2, quota.Used);
        Assert.Equal(5, quota.Remaining);
        Assert.Equal(StoryQuota.WeekStart(DateTime.UtcNow).AddDays(7), quota.ResetsAt);
    }

    [Fact]
    public async Task At_the_limit_the_chat_refuses_before_the_agent_is_called()
    {
        await ResetWithFinishedStoriesAsync(7);

        var response = await _client.PostAsJsonAsync(
            "/stories/conversations",
            new AgentMessageRequest(ConversationId: null, Message: "one more story"),
            Ct
        );

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.Contains("7 stories", problem!.Detail, StringComparison.Ordinal);
        Assert.Contains("Monday", problem.Detail, StringComparison.Ordinal);
        // The fake agent writes a conversation the moment it is called: none means it never was.
        Assert.False(await _factory.ExecuteDbAsync(db => db.Conversations.AnyAsync(Ct)));
    }

    [Fact]
    public async Task Below_the_limit_the_chat_carries_on()
    {
        await ResetWithFinishedStoriesAsync(6);

        var response = await _client.PostAsJsonAsync(
            "/stories/conversations",
            new AgentMessageRequest(ConversationId: null, Message: "one more story"),
            Ct
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task ResetWithFinishedStoriesAsync(int count)
    {
        await _factory.ResetAsync();
        var me = await _factory.ExecuteDbAsync(db => SeedData.SeedUserAsync(db, Me));
        await _factory.ExecuteDbAsync(async db =>
        {
            for (var i = 0; i < count; i++)
            {
                db.CompletedGenerations.Add(
                    new CompletedGeneration
                    {
                        Id = Guid.NewGuid(),
                        UserId = me,
                        CompletedAt = DateTime.UtcNow.AddMinutes(-1),
                    }
                );
            }
            await db.SaveChangesAsync(Ct);
        });
    }
}
