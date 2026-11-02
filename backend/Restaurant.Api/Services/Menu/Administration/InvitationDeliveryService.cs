using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.WebUtilities;

namespace Restaurant.Api.Services.Administration;

public sealed class InvitationDeliveryService
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public InvitationDeliveryService(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    // Development-only test mode. Token is returned only to an authenticated
    // restaurant administrator; never enable this in production.
    public bool DevelopmentTokenResponseEnabled =>
        _environment.IsDevelopment() &&
        _configuration.GetValue<bool>("Invitations:ExposeDevelopmentToken");

    public async Task SendAsync(string recipient, string token, CancellationToken ct)
    {
        if (DevelopmentTokenResponseEnabled)
            return; // The admin receives the dev token in the response.

        var baseUrl = _configuration["Invitations:AcceptUrl"];
        var host = _configuration["Invitations:Smtp:Host"];
        var from = _configuration["Invitations:Smtp:From"];
        var username = _configuration["Invitations:Smtp:User"];
        var password = _configuration["Invitations:Smtp:Password"];
        var port = _configuration.GetValue<int>("Invitations:Smtp:Port", 587);

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (!_environment.IsDevelopment() && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Invitation SMTP/acceptance URL is not configured.");

        var link = QueryHelpers.AddQueryString(baseUrl, "token", token);
        using var message = new MailMessage(from, recipient)
        {
            Subject = "Restaurant staff invitation",
            Body = "You have been invited to a restaurant. " +
                   "Open this link to accept within 48 hours:\n" + link,
            IsBodyHtml = false
        };
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true
        };
        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, password);

        await client.SendMailAsync(message, ct);
    }
}
