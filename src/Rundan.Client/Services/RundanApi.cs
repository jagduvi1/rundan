using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Client.Services;

/// <summary>
/// Typed gateway to the server API. Attaches the access code (and admin/participant
/// tokens where relevant) and turns failures into <see cref="ApiException"/>.
/// </summary>
public sealed class RundanApi(HttpClient http, AppState state)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // ---- Public bootstrap (not gated) --------------------------------------
    /// <summary>Fetches public app info. Returns null if the server is unreachable
    /// (so the caller treats it as "unknown", never as "no access code required").</summary>
    public async Task<BootstrapDto?> GetBootstrapAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<BootstrapDto>("api/bootstrap", JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns true if the given access code is accepted by the server.</summary>
    public async Task<bool> VerifyAccessCodeAsync(string code)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/session/verify");
        req.Headers.TryAddWithoutValidation("X-Rundan-Access", code);
        using var resp = await http.SendAsync(req);
        return resp.IsSuccessStatusCode;
    }

    /// <summary>Returns true if the current admin code is accepted (host-panel gate).</summary>
    public async Task<bool> VerifyAdminAsync()
    {
        try
        {
            await SendAsync<object>(HttpMethod.Get, "api/admin/verify", admin: true, expectBody: false);
            return true;
        }
        catch (ApiException)
        {
            return false;
        }
    }

    // ---- Roster users (admin) ----------------------------------------------
    public Task<List<UserDto>> ListUsersAsync() =>
        GetListAsync<UserDto>("api/users", admin: true);

    public async Task<UserDto> CreateUserAsync(string name) =>
        (await SendAsync<UserDto>(HttpMethod.Post, "api/users",
            body: new CreateUserRequest { Name = name }, admin: true))!;

    public async Task<UserDto> RenameUserAsync(int id, string name) =>
        (await SendAsync<UserDto>(HttpMethod.Put, $"api/users/{id}",
            body: new CreateUserRequest { Name = name }, admin: true))!;

    public Task DeleteUserAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/users/{id}", admin: true);

    // ---- Image upload (admin) ----------------------------------------------
    public async Task<string> UploadImageAsync(Stream content, string fileName, string contentType)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/admin/upload");
        if (!string.IsNullOrEmpty(state.AccessCode))
        {
            req.Headers.TryAddWithoutValidation("X-Rundan-Access", state.AccessCode);
        }

        if (state.IsHost)
        {
            req.Headers.TryAddWithoutValidation("X-Rundan-Admin", state.AdminCode ?? string.Empty);
        }

        if (state.ActiveMemberToken is { } memberToken)
        {
            req.Headers.TryAddWithoutValidation("X-Rundan-Member", memberToken.ToString());
        }

        var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        req.Content = form;

        using var resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            throw new ApiException(await ExtractMessageAsync(resp), (int)resp.StatusCode);
        }

        var result = await resp.Content.ReadFromJsonAsync<UploadResultDto>(JsonOptions);
        return result!.Url;
    }

    // ---- Events ------------------------------------------------------------
    public Task<EventDto?> GetEventAsync(int id) =>
        TryGetAsync<EventDto>($"api/events/{id}");

    public async Task<EventDto> UpdateEventAsync(int id, UpdateEventRequest request) =>
        (await SendAsync<EventDto>(HttpMethod.Put, $"api/events/{id}", body: request, admin: true))!;

    public async Task<EventDto> SetEventCodeAsync(int id, string? code) =>
        (await SendAsync<EventDto>(HttpMethod.Put, $"api/events/{id}/code",
            body: new SetEventCodeRequest { Code = code }, admin: true))!;

    public async Task<EventDto> SetEventMembersAsync(int id, List<int> userIds, List<int> adminUserIds) =>
        (await SendAsync<EventDto>(HttpMethod.Put, $"api/events/{id}/members",
            body: new SetEventMembersRequest { UserIds = userIds, AdminUserIds = adminUserIds }, admin: true))!;

    public async Task<ClaimResultDto> ClaimAsync(string code, int userId) =>
        (await SendAsync<ClaimResultDto>(HttpMethod.Post,
            $"api/events/by-code/{Uri.EscapeDataString(code.Trim())}/claim",
            body: new ClaimRequest { UserId = userId }))!;

    public Task<List<TeamDto>> GetTeamsAsync(int activityId) =>
        GetListAsync<TeamDto>($"api/activities/{activityId}/teams");

    /// <summary>The team line-up an event's roster currently forms (host view; the locked set in fixed mode).</summary>
    public Task<List<TeamDto>> GetEventTeamsAsync(int eventId) =>
        GetListAsync<TeamDto>($"api/events/{eventId}/teams", admin: true);

    /// <summary>Host re-rolls the locked fixed teams; returns the new line-up.</summary>
    public async Task<List<TeamDto>> ReshuffleTeamsAsync(int eventId) =>
        await SendAsync<List<TeamDto>>(HttpMethod.Post, $"api/events/{eventId}/teams/reshuffle", admin: true)
            ?? new();

    public Task<EventDto?> GetEventByCodeAsync(string code) =>
        TryGetAsync<EventDto>($"api/events/by-code/{Uri.EscapeDataString(code.Trim())}");

    public Task<EventStandingsDto?> GetEventStandingsAsync(int id) =>
        TryGetAsync<EventStandingsDto>($"api/events/{id}/standings");

    public async Task<EventJoinResultDto> JoinEventAsync(string code, string displayName) =>
        (await SendAsync<EventJoinResultDto>(
            HttpMethod.Post, $"api/events/by-code/{Uri.EscapeDataString(code.Trim())}/join",
            body: new EventJoinRequest { DisplayName = displayName }, admin: state.HasAdminCode))!;

    public Task<List<EventDto>> ListEventsAsync() =>
        GetListAsync<EventDto>("api/events", admin: true);

    /// <summary>Player-facing event list for the welcome page (no admin code needed).</summary>
    public Task<List<EventDto>> GetActiveEventsAsync() =>
        GetListAsync<EventDto>("api/events/active");

    // ---- Question library --------------------------------------------------
    public Task<List<string>> GetLibraryTagsAsync() =>
        GetListAsync<string>("api/question-library/tags");

    public async Task<int> GetLibraryAvailableAsync(List<string> tags)
    {
        var qs = tags.Count > 0 ? "?tags=" + Uri.EscapeDataString(string.Join(",", tags)) : string.Empty;
        return await SendAsync<int>(HttpMethod.Get, $"api/question-library/available{qs}");
    }

    public async Task<LibraryGenerateResult> GenerateFromLibraryAsync(int activityId, int count, List<string> tags) =>
        (await SendAsync<LibraryGenerateResult>(HttpMethod.Post, $"api/activities/{activityId}/questions/from-library",
            body: new LibraryGenerateRequest { Count = count, Tags = tags }, admin: true))!;

    // ---- Slaps -------------------------------------------------------------
    public async Task PerformSlapAsync(int eventId, int activityId, int slappedUserId, int? recipientUserId) =>
        await SendAsync<object>(HttpMethod.Post, $"api/events/{eventId}/slap",
            body: new PerformSlapRequest { ActivityId = activityId, SlappedUserId = slappedUserId, RecipientUserId = recipientUserId });

    public async Task SkipSlapAsync(int eventId, int activityId) =>
        await SendAsync<object>(HttpMethod.Post, $"api/events/{eventId}/slap/skip",
            body: new SkipSlapRequest { ActivityId = activityId }, admin: true);

    /// <summary>SlappedSends mode: the slapped player passes their lost points to a recipient.</summary>
    public async Task SendSlapPointsAsync(int eventId, int activityId, int recipientUserId) =>
        await SendAsync<object>(HttpMethod.Post, $"api/events/{eventId}/slap/send-points",
            body: new SendSlapPointsRequest { ActivityId = activityId, RecipientUserId = recipientUserId });

    public async Task<ViewerDto> RegisterViewerAsync(int eventId, string name, Guid? token) =>
        (await SendAsync<ViewerDto>(HttpMethod.Post, $"api/events/{eventId}/viewers",
            body: new RegisterViewerRequest { Name = name, Token = token }))!;

    public Task RemoveViewerAsync(int eventId, Guid token) =>
        SendAsync(HttpMethod.Delete, $"api/events/{eventId}/viewers/{token}");

    public async Task<EventDto> CreateEventAsync(CreateEventRequest request) =>
        (await SendAsync<EventDto>(HttpMethod.Post, "api/events", body: request, admin: true))!;

    public Task DeleteEventAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/events/{id}", admin: true);

    public Task ReorderActivitiesAsync(int eventId, List<int> activityIds) =>
        SendAsync(HttpMethod.Put, $"api/events/{eventId}/reorder",
            body: new ReorderActivitiesRequest { ActivityIds = activityIds }, admin: true);

    // ---- Activities (player) -----------------------------------------------
    public Task<ActivityDto?> GetActivityAsync(int id) =>
        TryGetAsync<ActivityDto>($"api/activities/{id}");

    public Task<ActivityDto?> GetActivityByCodeAsync(string code) =>
        TryGetAsync<ActivityDto>($"api/activities/by-code/{Uri.EscapeDataString(code.Trim())}");

    /// <summary>The slap ceremony for one finished activity (pending to take, or the resolved outcome).</summary>
    public Task<ActivitySlapDto?> GetActivitySlapAsync(int id) =>
        TryGetAsync<ActivitySlapDto>($"api/activities/{id}/slap");

    public Task<List<ParticipantDto>> GetParticipantsAsync(int id) =>
        GetListAsync<ParticipantDto>($"api/activities/{id}/participants");

    public Task<ScoreboardDto?> GetScoreboardAsync(int id) =>
        TryGetAsync<ScoreboardDto>($"api/activities/{id}/scoreboard");

    public async Task<JoinResultDto> JoinAsync(string code, JoinActivityRequest request)
    {
        // Send the admin code too, so the server can flag the joiner as an admin player.
        var result = await SendAsync<JoinResultDto>(
            HttpMethod.Post, $"api/activities/by-code/{Uri.EscapeDataString(code.Trim())}/join",
            body: request, admin: state.IsHost);
        return result!;
    }

    // ---- Questions / answers (player) --------------------------------------
    public Task<List<QuestionDto>> GetQuestionsAsync(int id) =>
        GetListAsync<QuestionDto>($"api/activities/{id}/questions");

    public Task<List<QuestionResultDto>> GetResultsAsync(int id) =>
        GetListAsync<QuestionResultDto>($"api/activities/{id}/results");

    /// <summary>Host fixes a question's correct answer after finish; re-scores everyone (host / event admin).</summary>
    public async Task<QuestionResultDto> UpdateAnswerKeyAsync(int id, int questionId, int? correctOptionId, string? acceptedText) =>
        (await SendAsync<QuestionResultDto>(HttpMethod.Put, $"api/activities/{id}/questions/{questionId}/answer-key",
            body: new UpdateAnswerKeyRequest { CorrectOptionId = correctOptionId, AcceptedFreeTextAnswer = acceptedText },
            admin: true))!;

    public Task<List<MyAnswerDto>> GetMyAnswersAsync(int id, Guid token) =>
        GetListAsync<MyAnswerDto>($"api/activities/{id}/my-answers", token);

    public async Task<AnswerResultDto> SubmitAnswerAsync(int id, SubmitAnswerRequest request, Guid token) =>
        (await SendAsync<AnswerResultDto>(
            HttpMethod.Post, $"api/activities/{id}/answers", body: request, participantToken: token))!;

    // ---- Scores (player) ---------------------------------------------------
    public Task<List<ScoreEntryDto>> GetScoresAsync(int id) =>
        GetListAsync<ScoreEntryDto>($"api/activities/{id}/scores");

    public async Task<ScoreEntryDto> RecordScoreAsync(int id, RecordScoreRequest request, Guid token) =>
        (await SendAsync<ScoreEntryDto>(
            HttpMethod.Post, $"api/activities/{id}/scores", body: request, participantToken: token))!;

    // ---- MapPin (player) ---------------------------------------------------
    public Task<List<MapCityDto>> GetMapCitiesAsync(int id, Guid token) =>
        GetListAsync<MapCityDto>($"api/activities/{id}/map-cities", participantToken: token);

    public async Task<MapPinResultDto> SubmitMapPinAsync(int id, MapPinRequest request, Guid token) =>
        (await SendAsync<MapPinResultDto>(
            HttpMethod.Post, $"api/activities/{id}/map-pin", body: request, participantToken: token))!;

    // ---- Dry run (simulate / clear results) --------------------------------
    public async Task<ActivityDto> SimulateActivityAsync(int id) =>
        (await SendAsync<ActivityDto>(HttpMethod.Post, $"api/activities/{id}/simulate", admin: true))!;

    public async Task<ActivityDto> ResetActivityResultsAsync(int id) =>
        (await SendAsync<ActivityDto>(HttpMethod.Post, $"api/activities/{id}/reset-results", admin: true))!;

    /// <summary>Destructive: wipe all data and re-seed the demo day (host "clean &amp; seed"). Needs the code.</summary>
    public Task CleanAndSeedAsync(string code) =>
        SendAsync(HttpMethod.Post, "api/admin/clean-and-seed",
            body: new CleanAndSeedRequest { Code = code }, admin: true);

    public async Task SimulateEventAsync(int eventId) =>
        await SendAsync<object>(HttpMethod.Post, $"api/events/{eventId}/simulate", admin: true);

    public async Task ResetEventResultsAsync(int eventId) =>
        await SendAsync<object>(HttpMethod.Post, $"api/events/{eventId}/reset-results", admin: true);

    public Task<List<ActivityDto>> GetLibraryAsync() =>
        GetListAsync<ActivityDto>("api/activities/library", admin: true);

    public async Task<ActivityDto> AddFromLibraryAsync(int eventId, int sourceId) =>
        (await SendAsync<ActivityDto>(HttpMethod.Post, $"api/events/{eventId}/activities/from-library/{sourceId}", admin: true))!;

    public async Task<ActivityDto> SetCourtsAsync(int id, string label, List<string> names) =>
        (await SendAsync<ActivityDto>(HttpMethod.Put, $"api/activities/{id}/courts",
            body: new SetCourtsRequest { Label = label, Names = names }, admin: true))!;

    // ---- Knockout bracket --------------------------------------------------
    public Task<BracketDto?> GetBracketAsync(int id) =>
        TryGetAsync<BracketDto>($"api/activities/{id}/bracket");

    public async Task<BracketDto> DrawBracketAsync(int id) =>
        (await SendAsync<BracketDto>(HttpMethod.Post, $"api/activities/{id}/bracket/draw", admin: true))!;

    public async Task<BracketDto> RecordBracketResultAsync(int id, int matchId, List<MatchSetInputDto> sets) =>
        (await SendAsync<BracketDto>(HttpMethod.Post, $"api/activities/{id}/bracket/result",
            body: new RecordBracketResultRequest { MatchId = matchId, Sets = sets }, admin: true))!;

    public async Task<BracketDto> ResetBracketAsync(int id) =>
        (await SendAsync<BracketDto>(HttpMethod.Post, $"api/activities/{id}/bracket/reset", admin: true))!;

    // ---- Word game ---------------------------------------------------------
    public async Task<WordGameDto?> GetWordGameAsync(int id, Guid token) =>
        await SendAsync<WordGameDto>(HttpMethod.Get, $"api/activities/{id}/wordgame", participantToken: token);

    public async Task<WordGameDto> SubmitWordAsync(int id, List<int> openedIndices, string word, Guid token) =>
        (await SendAsync<WordGameDto>(HttpMethod.Post, $"api/activities/{id}/wordgame",
            body: new SubmitWordRequest { OpenedIndices = openedIndices, Word = word }, participantToken: token))!;

    // ---- Admin -------------------------------------------------------------
    public Task<List<ActivityDto>> ListActivitiesAsync() =>
        GetListAsync<ActivityDto>("api/activities", admin: true);

    public async Task<ActivityDto> CreateActivityAsync(CreateActivityRequest request) =>
        (await SendAsync<ActivityDto>(HttpMethod.Post, "api/activities", body: request, admin: true))!;

    public async Task<ActivityDto> UpdateActivityAsync(int id, UpdateActivityRequest request) =>
        (await SendAsync<ActivityDto>(HttpMethod.Put, $"api/activities/{id}", body: request, admin: true))!;

    public async Task<ActivityDto> SetStatusAsync(int id, ActivityStatus status) =>
        (await SendAsync<ActivityDto>(HttpMethod.Put, $"api/activities/{id}/status",
            body: new UpdateActivityStatusRequest { Status = status }, admin: true))!;

    public Task DeleteActivityAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/activities/{id}", admin: true);

    public Task<List<QuestionAdminDto>> GetAdminQuestionsAsync(int id) =>
        GetListAsync<QuestionAdminDto>($"api/activities/{id}/questions/admin", admin: true);

    /// <summary>Sets how many stations a tipspromenad/quiz has — adds blank stations or trims empty ones.</summary>
    public async Task<List<QuestionAdminDto>> SetStationCountAsync(int id, int count) =>
        await SendAsync<List<QuestionAdminDto>>(HttpMethod.Put, $"api/activities/{id}/stations/count",
            body: new SetStationCountRequest { Count = count }, admin: true) ?? new();

    public async Task<QuestionAdminDto> AddQuestionAsync(int id, QuestionUpsertRequest request) =>
        (await SendAsync<QuestionAdminDto>(HttpMethod.Post, $"api/activities/{id}/questions",
            body: request, admin: true))!;

    public async Task<QuestionAdminDto> UpdateQuestionAsync(int id, int questionId, QuestionUpsertRequest request) =>
        (await SendAsync<QuestionAdminDto>(HttpMethod.Put, $"api/activities/{id}/questions/{questionId}",
            body: request, admin: true))!;

    public Task DeleteQuestionAsync(int id, int questionId) =>
        SendAsync(HttpMethod.Delete, $"api/activities/{id}/questions/{questionId}", admin: true);

    /// <summary>Set/clear one question's GPS station (per-question map for a tipspromenad).</summary>
    public async Task<QuestionAdminDto> SetQuestionLocationAsync(int id, int questionId, double? lat, double? lng, int? radius) =>
        (await SendAsync<QuestionAdminDto>(HttpMethod.Put, $"api/activities/{id}/questions/{questionId}/location",
            body: new SetQuestionLocationRequest { Latitude = lat, Longitude = lng, RadiusMeters = radius }, admin: true))!;

    public Task KickParticipantAsync(int id, int participantId) =>
        SendAsync(HttpMethod.Delete, $"api/activities/{id}/participants/{participantId}", admin: true);

    public Task DeleteScoreAsync(int id, int scoreId) =>
        SendAsync(HttpMethod.Delete, $"api/activities/{id}/scores/{scoreId}", admin: true);

    // ---- Plumbing ----------------------------------------------------------
    private async Task<List<T>> GetListAsync<T>(string url, Guid? participantToken = null, bool admin = false)
        => await SendAsync<List<T>>(HttpMethod.Get, url, participantToken: participantToken, admin: admin)
           ?? new List<T>();

    private async Task<T?> TryGetAsync<T>(string url)
    {
        try
        {
            return await SendAsync<T>(HttpMethod.Get, url);
        }
        catch (ApiException ex) when (ex.IsNotFound)
        {
            return default;
        }
    }

    private async Task SendAsync(
        HttpMethod method, string url, object? body = null, Guid? participantToken = null, bool admin = false)
        => await SendAsync<object>(method, url, body, participantToken, admin, expectBody: false);

    private async Task<T?> SendAsync<T>(
        HttpMethod method, string url, object? body = null, Guid? participantToken = null,
        bool admin = false, bool expectBody = true)
    {
        using var req = new HttpRequestMessage(method, url);

        if (!string.IsNullOrEmpty(state.AccessCode))
        {
            req.Headers.TryAddWithoutValidation("X-Rundan-Access", state.AccessCode);
        }

        if (admin && state.IsHost)
        {
            // In open-admin mode there's no stored code; the server accepts an empty header.
            req.Headers.TryAddWithoutValidation("X-Rundan-Admin", state.AdminCode ?? string.Empty);
        }

        if (participantToken is { } token)
        {
            req.Headers.TryAddWithoutValidation("X-Rundan-Participant", token.ToString());
        }

        // Event-admin credential (lets a promoted participant manage their event).
        if (state.ActiveMemberToken is { } memberToken)
        {
            req.Headers.TryAddWithoutValidation("X-Rundan-Member", memberToken.ToString());
        }

        if (body is not null)
        {
            req.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var resp = await http.SendAsync(req);

        if (!resp.IsSuccessStatusCode)
        {
            throw new ApiException(await ExtractMessageAsync(resp), (int)resp.StatusCode);
        }

        if (!expectBody || resp.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        return await resp.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    private static async Task<string> ExtractMessageAsync(HttpResponseMessage resp)
    {
        var fallback = $"Something went wrong ({(int)resp.StatusCode}).";
        try
        {
            var raw = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "detail", "error", "title" })
                {
                    if (doc.RootElement.TryGetProperty(name, out var prop) &&
                        prop.ValueKind == JsonValueKind.String)
                    {
                        var value = prop.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }
            }
        }
        catch
        {
            // not JSON; fall through
        }

        return fallback;
    }
}
