using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace TradingCompany.LiveExecution;

public class LiveProgram
{
    public const string OwnerScheme = "QuantaraOwner";
    private const string OwnerOrigin = "https://localhost:3000";

    public static void Main(string[] args)
    {
        if (args.SequenceEqual(["--activate-full-spot-live"]))
        {
            Environment.ExitCode = LiveOwnerActivation.RunCommandAsync().GetAwaiter().GetResult();
            return;
        }
        if (args.SequenceEqual(["--verify-live-configuration"]))
        {
            Environment.ExitCode = LiveOwnerActivation.RunCommandAsync(activate: false).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--pair-owner-client", StringComparer.Ordinal))
        {
            if (args.Length != 1 || Console.IsOutputRedirected || Console.IsInputRedirected)
            {
                Console.Error.WriteLine("Owner pairing requires a local interactive console.");
                Environment.ExitCode = 2;
                return;
            }
            try
            {
                var code = OwnerPairingConsole.RequestCodeAsync().GetAwaiter().GetResult();
                Console.WriteLine($"Owner pairing code (expires in 5 minutes): {code}");
                Console.WriteLine("Paste the code into Live at https://localhost:3000. Do not send it in chat.");
                Console.WriteLine("You may close this window after pairing. Quantara keeps running in the background.");
            }
            catch
            {
                Console.Error.WriteLine("Local pairing service unavailable. Start Quantara and try again.");
                Environment.ExitCode = 1;
            }
            return;
        }
        var pairLocally = args.Contains("--pair-owner", StringComparer.Ordinal);
        if (pairLocally && (Console.IsOutputRedirected || Console.IsInputRedirected))
            throw new InvalidOperationException("Owner pairing requires a local interactive console.");
        var builder = WebApplication.CreateBuilder(args.Where(a => a != "--pair-owner").ToArray());
        var serveOwnerWeb = builder.Configuration.GetValue<bool>("OwnerWeb:ProxyEnabled");
        var containerHost = builder.Configuration.GetValue<bool>("OwnerWeb:ContainerHost");
        if (containerHost && Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
            throw new InvalidOperationException("Container binding requires the container runtime.");
        builder.WebHost.ConfigureKestrel(options =>
        {
            // Bounded eight-role meeting reports use the same authenticated TLS listener.
            options.Limits.MaxRequestBodySize = 64 * 1024;
            if (containerHost)
            {
                options.ListenAnyIP(7443, endpoint => endpoint.UseHttps());
                if (serveOwnerWeb)
                {
                    options.ListenAnyIP(3000, endpoint => endpoint.UseHttps());
                    options.ListenAnyIP(2080);
                }
            }
            else
            {
                options.ListenLocalhost(7443, endpoint => endpoint.UseHttps());
                if (serveOwnerWeb)
                {
                    options.ListenLocalhost(3000, endpoint => endpoint.UseHttps());
                    options.ListenLocalhost(2080);
                }
            }
        });
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<LiveActivityStore>();
        builder.Services.AddHttpClient("owner-web", client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        var walletCredentialsPath = builder.Configuration["Bitkub:CredentialsFile"];
        if (!string.IsNullOrWhiteSpace(walletCredentialsPath))
            builder.Services.AddSingleton(sp => new TradingCompany.Infrastructure.BitkubWalletReader(
                Path.GetFullPath(walletCredentialsPath), sp.GetRequiredService<TimeProvider>()));
        BitkubLiveRuntime.Configure(builder.Services, builder.Configuration);
        builder.Services.AddHostedService(sp => new LiveReconciliationWorker(
            sp.GetService<TradingCompany.Application.LiveExecutionService>(),
            sp.GetService<TradingCompany.Application.LiveReconciliation>(),
            sp.GetService<BitkubConfiguredAccount>()?.AccountId ?? default,
            sp.GetRequiredService<TimeProvider>(), sp.GetService<TradingCompany.Infrastructure.LiveTradingStore>()));
        var ownerStateDirectory = builder.Configuration["OwnerSession:StateDirectory"];
        if (!string.IsNullOrWhiteSpace(ownerStateDirectory))
        {
            X509Certificate2 certificate;
            try
            {
                certificate = X509CertificateLoader.LoadPkcs12FromFile(
                    builder.Configuration["Kestrel:Certificates:Default:Path"]!,
                    builder.Configuration["Kestrel:Certificates:Default:Password"],
                    X509KeyStorageFlags.EphemeralKeySet);
                if (!certificate.HasPrivateKey) throw new InvalidOperationException();
            }
            catch { throw new InvalidOperationException("Protected owner session storage requires its HTTPS certificate."); }
            builder.Services.AddDataProtection().SetApplicationName("Quantara.Owner.v1")
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(ownerStateDirectory, "keys")))
                .ProtectKeysWithCertificate(certificate);
        }
        else builder.Services.AddSingleton<IDataProtectionProvider>(_ => new EphemeralDataProtectionProvider());
        builder.Services.AddSingleton(sp => new OwnerSession(sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IDataProtectionProvider>(), string.IsNullOrWhiteSpace(ownerStateDirectory) ? null : ownerStateDirectory));
        if (builder.Configuration.GetValue<bool>("OwnerConsole:Enabled"))
            builder.Services.AddHostedService(sp => new OwnerPairingConsole(sp.GetRequiredService<OwnerSession>()));
        builder.Services.AddAuthentication(OwnerScheme).AddCookie(OwnerScheme, options =>
        {
            options.Cookie.Name = "__Host-QuantaraOwner";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; },
                OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; },
                OnValidatePrincipal = context =>
                {
                    if (!context.HttpContext.RequestServices.GetRequiredService<OwnerSession>()
                        .IsActive(context.Principal?.FindFirstValue("sid"))) context.RejectPrincipal();
                    return Task.CompletedTask;
                }
            };
        });
        builder.Services.AddOptions<CookieAuthenticationOptions>(OwnerScheme)
            .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock);
        // Dedicated advisory identity. Missing explicit token/account configuration has no access.
        builder.Services.AddAuthentication().AddScheme<CoordinatorAccessOptions, CoordinatorAccess>(CoordinatorAccess.SchemeName, _ => { });
        builder.Services.AddAuthorizationBuilder().AddPolicy("Coordinator", policy =>
            policy.AddAuthenticationSchemes(CoordinatorAccess.SchemeName).RequireAuthenticatedUser().RequireRole("Coordinator"));
        builder.Services.AddAuthorizationBuilder().AddPolicy("Owner", policy =>
            policy.AddAuthenticationSchemes(OwnerScheme).RequireAuthenticatedUser().RequireRole("Owner")
                .RequireAssertion(context => context.Resource is HttpContext http &&
                    http.RequestServices.GetRequiredService<OwnerSession>().CanControl(context.User.FindFirstValue("sid"))));
        builder.Services.AddAuthorizationBuilder().AddPolicy("OwnerRead", policy =>
            policy.AddAuthenticationSchemes(OwnerScheme).RequireAuthenticatedUser().RequireRole("Owner"));
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-QuantaraCsrf";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.WithOrigins(OwnerOrigin).WithMethods("GET", "POST")
                .WithHeaders("Content-Type", "X-CSRF-TOKEN").AllowCredentials()));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            if (serveOwnerWeb && context.Connection.LocalPort == 2080)
            {
                OwnerLaunchRedirect.Handle(context);
                return;
            }
            if (serveOwnerWeb && context.Request.Host.Value == "localhost:3000")
            {
                using var frontend = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("owner-web");
                await OwnerWebProxy.ForwardAsync(context, frontend);
                return;
            }
            var origin = context.Request.Headers.Origin;
            var coordinatorRoute = context.Request.Path.StartsWithSegments("/internal/live");
            var mutating = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
                && !HttpMethods.IsOptions(context.Request.Method);
            if (!context.Request.IsHttps || context.Request.Host.Value != "localhost:7443" ||
                (origin.Count > 0 && (origin.Count != 1 || origin[0] != OwnerOrigin)) ||
                (coordinatorRoute && origin.Count != 0) || (mutating && !coordinatorRoute && origin.Count != 1))
            {
                context.Response.StatusCode = 403;
                return;
            }
            await next(context);
        });
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) && !context.Request.Path.StartsWithSegments("/internal/live"))
            {
                try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                catch (AntiforgeryValidationException) { context.Response.StatusCode = 403; return; }
            }
            await next(context);
        });
        app.MapGet("/owner/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
        app.MapPost("/owner/pair", async (PairRequest request, OwnerSession sessions, HttpContext context) =>
        {
            (int Status, string? SessionId) result;
            try { result = sessions.TryPair(request.Code, request.RememberBrowser); }
            catch { return Results.StatusCode(503); }
            if (result.Status != 204) return Results.StatusCode(result.Status);
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "owner"), new Claim(ClaimTypes.Role, "Owner"),
                new Claim("sid", result.SessionId!)
            };
            await context.SignInAsync(OwnerScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, OwnerScheme)),
                new AuthenticationProperties { IsPersistent = request.RememberBrowser, AllowRefresh = false,
                    ExpiresUtc = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow()
                        .Add(request.RememberBrowser ? OwnerSession.ViewingLifetime : OwnerSession.ControlLifetime) });
            return Results.NoContent();
        });
        app.MapPost("/owner/logout", async (OwnerSession sessions, HttpContext context) =>
        {
            try { sessions.Revoke(context.User.FindFirstValue("sid")); }
            catch { return Results.StatusCode(503); }
            await context.SignOutAsync(OwnerScheme);
            return Results.NoContent();
        }).RequireAuthorization("OwnerRead");
        // No production broker/store registration until provisioning and recovery are verified.
        app.MapLiveEndpoints();
        app.MapCoordinatorEndpoints();
        if (pairLocally)
            Console.WriteLine($"Owner pairing code (expires in 5 minutes): {app.Services.GetRequiredService<OwnerSession>().IssuePairingCode()}");
        app.Run();
    }

    public sealed record PairRequest(string? Code, bool RememberBrowser = false);
}
