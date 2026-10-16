using System.Net;
using System.Net.Http.Json;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ComplaintSystem.IntegrationTests;

public sealed class AuthenticationAndComplaintTests : IClassFixture<ComplaintWebApplicationFactory>
{
    private readonly ComplaintWebApplicationFactory _factory;

    public AuthenticationAndComplaintTests(ComplaintWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Student_can_register_verify_login_and_submit_complaint()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });
        var csrf = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        Assert.NotNull(csrf);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.Token);

        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterStudentRequest(
            "ce-kojo.ansah0922@st.umat.edu.gh", "Secure!Pass12345", "Secure!Pass12345",
            "Kojo", "Ansah", "9012661223", "+233241234567",
            "Computer Science and Engineering", "400",
            new DateOnly(2001, 5, 12)));
        Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);

        var verificationUrl = _factory.Services.GetRequiredService<FakeEmailSender>().LastVerificationUrl;
        Assert.NotNull(verificationUrl);
        var verificationUri = new Uri(verificationUrl);
        var query = QueryHelpers.ParseQuery(verificationUri.Query);
        var verify = await client.GetAsync($"/api/auth/verify-email?userId={query["userId"]}&token={Uri.EscapeDataString(query["token"]!)}");
        Assert.Equal(HttpStatusCode.NoContent, verify.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/student/login",
            new LoginRequest("ce-kojo.ansah0922@st.umat.edu.gh", "Secure!Pass12345"));
        login.EnsureSuccessStatusCode();
        var loginBody = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.True(loginBody?.Authenticated);
        var authenticatedCsrf = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", authenticatedCsrf!.Token);

        Guid departmentId;
        Guid categoryId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var category = await db.ComplaintCategories.AsNoTracking().FirstAsync();
            categoryId = category.Id;
            departmentId = category.DepartmentId!.Value;
        }
        var submission = await client.PostAsJsonAsync("/api/complaints", new SubmitComplaintRequest(
            "Unable to access the student portal",
            "The student portal has rejected my valid university account since Monday morning.",
            categoryId, departmentId));
        submission.EnsureSuccessStatusCode();
        var created = await submission.Content.ReadFromJsonAsync<ComplaintCreatedResponse>();
        Assert.StartsWith("UMAT-", created?.ComplaintId);
        Assert.Null(created?.TrackingCode);
    }

    [Fact]
    public async Task Current_registration_form_can_login_by_reference_and_open_dashboard_profile()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false
        });
        var csrf = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        const string password = "Secure!Current123";
        var registration = new RegisterStudentRequest(null, password, password, "Yaw", "Asare", "9015555526",
            "+233241112222", "Mechanical Engineering", "200", new DateOnly(2003, 3, 15));

        Assert.Equal(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/api/auth/register", registration)).StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/student/login",
            new LoginRequest("9015555526", password));
        login.EnsureSuccessStatusCode();
        Assert.True((await login.Content.ReadFromJsonAsync<AuthResponse>())!.Authenticated);

        var profile = await client.GetFromJsonAsync<CurrentUserDto>("/api/students/me");
        Assert.Equal("9015555526", profile!.ReferenceNumber);
        Assert.Equal("Yaw", profile.FirstName);
    }

    [Fact]
    public async Task Student_authentication_enforces_uniqueness_role_profile_and_logout()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false
        });
        var csrf = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        const string email = "ds-efua.authcase26@st.umat.edu.gh";
        const string password = "Secure!Student123";
        var request = new RegisterStudentRequest(email, password, password, "Efua", "Owusu", "9012661288",
            "+233501112233", "Data Science and Analytics", "300",
            new DateOnly(2002, 8, 20));

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", request with
        {
            ReferenceNumber = "9012661289"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", request with
        {
            Email = "ds-efua.other26@st.umat.edu.gh"
        })).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Users.SingleAsync(x => x.Email == email);
            Assert.NotEqual(password, stored.PasswordHash);
            Assert.DoesNotContain(password, stored.PasswordHash ?? string.Empty);
            Assert.StartsWith("$2", stored.PasswordHash);
        }

        var verificationUrl = _factory.Services.GetRequiredService<FakeEmailSender>().LastVerificationUrl!;
        var query = QueryHelpers.ParseQuery(new Uri(verificationUrl).Query);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync(
            $"/api/auth/verify-email?userId={query["userId"]}&token={Uri.EscapeDataString(query["token"]!)}")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/student/login",
            new LoginRequest(email, "Wrong!Password123"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/student/login",
            new LoginRequest("9012661299", password))).StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/student/login", new LoginRequest(" 9012661288 ", password));
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.True(body!.Authenticated);
        Assert.Contains("Student", body.User!.Roles);

        var profile = await client.GetFromJsonAsync<CurrentUserDto>("/api/students/me");
        Assert.Equal(email, profile!.Email);
        Assert.Equal("+233501112233", profile.TelephoneNumber);
        Assert.Equal("300", profile.Level);
        Assert.Equal(new DateOnly(2002, 8, 20), profile.DateOfBirth);

        var authenticatedCsrf = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", authenticatedCsrf!.Token);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/students/me")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_tracking_rejects_an_incorrect_code()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = true
        });
        var csrf = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        Guid departmentId;
        Guid categoryId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var category = await db.ComplaintCategories.AsNoTracking().FirstAsync(x => x.AllowsAnonymous);
            categoryId = category.Id;
            departmentId = category.DepartmentId!.Value;
        }
        var submit = await client.PostAsJsonAsync("/api/anonymous-complaints", new SubmitAnonymousComplaintRequest(
            "Anonymous welfare concern", "This is a sufficiently detailed anonymous student welfare concern for investigation.",
            categoryId, departmentId));
        submit.EnsureSuccessStatusCode();
        var created = await submit.Content.ReadFromJsonAsync<ComplaintCreatedResponse>();
        var track = await client.PostAsJsonAsync("/api/anonymous-complaints/track",
            new AnonymousTrackRequest(created!.ComplaintId, "incorrect-tracking-code"));
        Assert.Equal(HttpStatusCode.BadRequest, track.StatusCode);
    }
}
