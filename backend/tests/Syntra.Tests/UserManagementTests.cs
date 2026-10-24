using System.Net;
using System.Net.Http.Json;

namespace Syntra.Tests;

public class UserManagementTests : IClassFixture<TenantIsolationFixture>
{
    private const string Password = "P@ssw0rd123!";

    private readonly TenantIsolationFixture _fixture;

    public UserManagementTests(TenantIsolationFixture fixture)
    {
        _fixture = fixture;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, object? body = null)
    {
        var request = _fixture.CreateRequest(method, _fixture.TenantASlug, path, token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _fixture.Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> AsAdmin(HttpMethod method, string path, object? body = null) =>
        SendAsync(method, path, _fixture.TenantAToken, body);

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@isolation-test.local";

    private async Task<UserDto> CreateUserAsync(bool isAdmin = false, int[]? branchIds = null)
    {
        var response = await AsAdmin(HttpMethod.Post, "/tenancy/users", new
        {
            email = NewEmail(),
            password = Password,
            isAdmin,
            branchIds = branchIds ?? []
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<UserDto>())!;
    }

    private async Task<HttpResponseMessage> LoginResponseAsync(string email, string password)
    {
        var request = _fixture.CreateRequest(HttpMethod.Post, _fixture.TenantASlug, "/auth/login");
        request.Content = JsonContent.Create(new { email, password });
        return await _fixture.Client.SendAsync(request);
    }

    private async Task<string> LoginAsync(string email, string password = Password)
    {
        var response = await LoginResponseAsync(email, password);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenDto>())!.Token;
    }

    private async Task<int> BranchIdAsync(string code)
    {
        var response = await AsAdmin(HttpMethod.Post, "/tenancy/branches", new { code, name = code });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BranchDto>())!.BranchId;
    }

    [Fact]
    public async Task NonAdmin_IsForbiddenFromUserApiAndBranchWrites()
    {
        var staff = await CreateUserAsync();
        var token = await LoginAsync(staff.Email);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/tenancy/users", token)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsync(HttpMethod.Post, "/tenancy/branches", token, new { code = "nope", name = "Nope" })).StatusCode);

        // Reading branches stays open to any signed-in user.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/tenancy/branches", token)).StatusCode);
    }

    [Fact]
    public async Task DuplicateEmail_ReturnsConflict()
    {
        var existing = await CreateUserAsync();

        var response = await AsAdmin(HttpMethod.Post, "/tenancy/users", new
        {
            email = existing.Email,
            password = Password,
            isAdmin = false,
            branchIds = Array.Empty<int>()
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task WeakPassword_IsRejected()
    {
        var response = await AsAdmin(HttpMethod.Post, "/tenancy/users", new
        {
            email = NewEmail(),
            password = "abc",
            isAdmin = false,
            branchIds = Array.Empty<int>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeactivatedUser_IsBlockedImmediatelyEvenWithValidToken()
    {
        var staff = await CreateUserAsync();
        var token = await LoginAsync(staff.Email);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/auth/me", token)).StatusCode);

        var deactivate = await AsAdmin(HttpMethod.Put, $"/tenancy/users/{staff.UserId}/active", new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/auth/me", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginResponseAsync(staff.Email, Password)).StatusCode);

        var reactivate = await AsAdmin(HttpMethod.Put, $"/tenancy/users/{staff.UserId}/active", new { isActive = true });
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/auth/me", token)).StatusCode);
    }

    [Fact]
    public async Task DemotedAdmin_LosesAdminAccessImmediately()
    {
        var second = await CreateUserAsync(isAdmin: true);
        var token = await LoginAsync(second.Email);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/tenancy/users", token)).StatusCode);

        var demote = await AsAdmin(HttpMethod.Put, $"/tenancy/users/{second.UserId}/admin", new { isAdmin = false });
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);

        // Same token, which still carries the Admin role claim.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/tenancy/users", token)).StatusCode);
    }

    [Fact]
    public async Task Admin_CannotDeactivateOrDemoteThemselves()
    {
        var me = await (await AsAdmin(HttpMethod.Get, "/auth/me")).Content.ReadFromJsonAsync<MeDto>();
        var id = int.Parse(me!.UserId!);

        var deactivate = await AsAdmin(HttpMethod.Put, $"/tenancy/users/{id}/active", new { isActive = false });
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);

        var demote = await AsAdmin(HttpMethod.Put, $"/tenancy/users/{id}/admin", new { isAdmin = false });
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
    }

    [Fact]
    public async Task SetBranches_ReplacesTheSetAndValidatesIds()
    {
        var first = await BranchIdAsync($"b1-{Guid.NewGuid():N}"[..12]);
        var second = await BranchIdAsync($"b2-{Guid.NewGuid():N}"[..12]);
        var staff = await CreateUserAsync(branchIds: [first]);
        Assert.Equal([first], staff.BranchIds);

        var replaced = await AsAdmin(HttpMethod.Put, $"/tenancy/users/{staff.UserId}/branches", new { branchIds = new[] { second } });
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal([second], (await replaced.Content.ReadFromJsonAsync<UserDto>())!.BranchIds);

        var invalid = await AsAdmin(HttpMethod.Put, $"/tenancy/users/{staff.UserId}/branches", new { branchIds = new[] { 99999 } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        // The invalid request must not have touched the existing set.
        var unchanged = await (await AsAdmin(HttpMethod.Get, $"/tenancy/users/{staff.UserId}")).Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal([second], unchanged!.BranchIds);
    }

    [Fact]
    public async Task Me_ReportsIsAdminAndBranchIds()
    {
        var branch = await BranchIdAsync($"me-{Guid.NewGuid():N}"[..12]);
        var staff = await CreateUserAsync(branchIds: [branch]);
        var token = await LoginAsync(staff.Email);

        var staffMe = await (await SendAsync(HttpMethod.Get, "/auth/me", token)).Content.ReadFromJsonAsync<MeDto>();
        Assert.False(staffMe!.IsAdmin);
        Assert.Equal([branch], staffMe.BranchIds);

        var adminMe = await (await AsAdmin(HttpMethod.Get, "/auth/me")).Content.ReadFromJsonAsync<MeDto>();
        Assert.True(adminMe!.IsAdmin);
    }

    [Fact]
    public async Task ChangePassword_RequiresCurrentPasswordAndSwitchesLogin()
    {
        var staff = await CreateUserAsync();
        var token = await LoginAsync(staff.Email);
        const string newPassword = "N3w-P@ssword!";

        var wrongCurrent = await SendAsync(HttpMethod.Post, "/auth/change-password", token,
            new { currentPassword = "wrong", newPassword });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);

        var changed = await SendAsync(HttpMethod.Post, "/auth/change-password", token,
            new { currentPassword = Password, newPassword });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginResponseAsync(staff.Email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginResponseAsync(staff.Email, newPassword)).StatusCode);
    }

    [Fact]
    public async Task AdminResetPassword_ReplacesThePassword()
    {
        var staff = await CreateUserAsync();
        const string resetTo = "R3set-P@ssword!";

        var reset = await AsAdmin(HttpMethod.Post, $"/tenancy/users/{staff.UserId}/reset-password", new { newPassword = resetTo });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginResponseAsync(staff.Email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginResponseAsync(staff.Email, resetTo)).StatusCode);
    }

    private record UserDto(int UserId, string Email, bool IsActive, bool IsAdmin, int[] BranchIds);
    private record TokenDto(string Token);
    private record BranchDto(int BranchId);
    private record MeDto(string? UserId, bool IsAdmin, int[] BranchIds);
}
