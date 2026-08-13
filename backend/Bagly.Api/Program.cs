using System.Text;
using Bagly.Api.Data;
using Bagly.Api.Hubs;
using Bagly.Api.Middleware;
using Bagly.Api.Options;
using Bagly.Api.Services;
using Bagly.Api.Services.Chat;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    var baglyCsSet = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BAGLY_CONNECTION_STRING"));
    var aspNetCsSet = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));
    Log.Information(
        "Render DB env check: BAGLY_CONNECTION_STRING={BaglySet}, ConnectionStrings__DefaultConnection={AspNetSet}",
        baglyCsSet,
        aspNetCsSet);

    var connectionString = ResolveConnectionString(builder.Configuration);
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Log.Error(
            "Database connection string is missing. In Render → Environment add KEY exactly 'BAGLY_CONNECTION_STRING' (or 'ConnectionStrings__DefaultConnection') with your Neon Postgres connection string, then Manual Deploy.");
        // Keep process alive so /api/health can report what's missing.
        connectionString =
            "Host=127.0.0.1;Port=5432;Database=missing;Username=missing;Password=missing;Timeout=1;";
    }

    try
    {
        var host = new NpgsqlConnectionStringBuilder(connectionString).Host;
        Log.Information("Using Postgres host: {Host}", host);
        if (!string.IsNullOrWhiteSpace(host) &&
            (host.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
             host.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
        {
            Log.Warning("Postgres host looks local/placeholder ({Host}). Set the Neon connection string on Render env vars and redeploy.", host);
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Connection string could not be parsed. Check Render env var value.");
    }

    // Ensure EF + the rest of config see the resolved value (overrides appsettings.json).
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = connectionString,
    });

    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "Bagly.Api")
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .WriteTo.Console();
    });

    builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
    builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
    builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
    builder.Services.Configure<RazorpayOptions>(builder.Configuration.GetSection(RazorpayOptions.SectionName));
    builder.Services.Configure<ShiprocketOptions>(builder.Configuration.GetSection(ShiprocketOptions.SectionName));
    builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
    builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.SectionName));
    builder.Services.Configure<ChatOptions>(builder.Configuration.GetSection(ChatOptions.SectionName));
    builder.Services.Configure<CloudinaryOptions>(builder.Configuration.GetSection(CloudinaryOptions.SectionName));
    builder.Services.Configure<StorefrontOptions>(builder.Configuration.GetSection(StorefrontOptions.SectionName));
    builder.Services.AddSingleton<TokenService>();
    builder.Services.AddSingleton<ICloudinaryImageService, CloudinaryImageService>();
    builder.Services.AddScoped<IAuditLogService, AuditLogService>();
    builder.Services.AddScoped<IPaymentLogService, PaymentLogService>();
    builder.Services.AddSingleton<EmailDeliveryDiagnostics>();
    builder.Services.AddScoped<IEmailSender, EmailSender>();
    builder.Services.AddScoped<IOrderConfirmationEmailService, OrderConfirmationEmailService>();
    builder.Services.AddScoped<IContactEmailService, ContactEmailService>();
    builder.Services.AddScoped<ISellerApprovalEmailService, SellerApprovalEmailService>();
    builder.Services.AddSingleton<IContactRateLimiter, ContactRateLimiter>();
    builder.Services.AddSingleton<ISiteHitRateLimiter, SiteHitRateLimiter>();
    builder.Services.Configure<GeoIpOptions>(builder.Configuration.GetSection(GeoIpOptions.SectionName));
    builder.Services.AddSingleton<IIpGeolocationService, IpGeolocationService>();
    builder.Services.AddHttpClient("GeoIpIpWhoIs", client =>
    {
        client.BaseAddress = new Uri("https://ipwho.is/");
        client.Timeout = TimeSpan.FromSeconds(3);
    });
    builder.Services.AddHttpClient("GeoIpGeoJs", client =>
    {
        client.BaseAddress = new Uri("https://get.geojs.io/");
        client.Timeout = TimeSpan.FromSeconds(3);
    });
    builder.Services.AddSingleton<IOrderConfirmationEmailDispatcher, OrderConfirmationEmailDispatcher>();
    builder.Services.AddHostedService(sp => (OrderConfirmationEmailDispatcher)sp.GetRequiredService<IOrderConfirmationEmailDispatcher>());
    builder.Services.AddSingleton<ShiprocketTokenStore>();
    builder.Services.AddScoped<IShiprocketService, ShiprocketService>();
    builder.Services.AddSingleton<IShiprocketOrderDispatcher, ShiprocketOrderDispatcher>();
    builder.Services.AddHostedService(sp => (ShiprocketOrderDispatcher)sp.GetRequiredService<IShiprocketOrderDispatcher>());
    builder.Services.AddScoped<IStockAlertNotifier, StockAlertNotifier>();
    builder.Services.AddSingleton<IStockAlertNotificationDispatcher, StockAlertNotificationDispatcher>();
    builder.Services.AddHostedService(sp => (StockAlertNotificationDispatcher)sp.GetRequiredService<IStockAlertNotificationDispatcher>());
    builder.Services.AddHostedService<StockAlertPollingService>();
    builder.Services.AddHttpClient("SendGrid", client =>
    {
        client.BaseAddress = new Uri("https://api.sendgrid.com/");
        client.Timeout = TimeSpan.FromSeconds(30);
    });
    builder.Services.AddHttpClient("Resend", client =>
    {
        client.BaseAddress = new Uri("https://api.resend.com/");
        client.Timeout = TimeSpan.FromSeconds(30);
    });
    builder.Services.AddHttpClient("Shiprocket", (sp, client) =>
    {
        var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ShiprocketOptions>>().Value;
        var baseUrl = string.IsNullOrWhiteSpace(opts.BaseUrl)
            ? "https://apiv2.shiprocket.in"
            : opts.BaseUrl.Trim().TrimEnd('/');
        client.BaseAddress = new Uri(baseUrl + "/");
        client.Timeout = TimeSpan.FromSeconds(30);
    });
    builder.Services.AddHttpClient<IRazorpayService, RazorpayService>();
    builder.Services.AddHttpClient<IOpenAiChatClient, OpenAiChatClient>();

    builder.Services.AddSignalR();
    builder.Services.AddSingleton<IChatSessionStore, InMemoryChatSessionStore>();
    builder.Services.AddSingleton<IChatConversationRegistry, ChatConversationRegistry>();
    builder.Services.AddSingleton<IChatRateLimiter, ChatRateLimiter>();
    builder.Services.AddScoped<IChatToolExecutor, ChatToolExecutor>();
    builder.Services.AddScoped<IRuleBasedChatResponder, RuleBasedChatResponder>();
    builder.Services.AddScoped<IChatAgentService, ChatAgentService>();

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Bagly API",
            Version = "v1",
            Description = "E-commerce API for the Bagly online bags storefront.",
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter JWT token from /api/auth/login",
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer",
                    },
                },
                Array.Empty<string>()
            },
        });
    });

    var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
        ?? throw new InvalidOperationException("Jwt configuration is missing.");

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                ClockSkew = TimeSpan.FromMinutes(1),
                RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            };

            // SignalR browser clients cannot set custom headers on WebSocket upgrades,
            // so the JWT is sent via the "access_token" query string param for /hubs/* instead.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                },
            };
        });

    builder.Services.AddAuthorization();

    builder.Services.AddDbContext<BaglyDbContext>(options =>
        options.UseNpgsql(connectionString));

    builder.Services.AddCors(options =>
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? [];

        options.AddPolicy("Frontend", policy =>
        {
            policy.SetIsOriginAllowed(origin =>
                {
                    if (string.IsNullOrWhiteSpace(origin))
                    {
                        return false;
                    }

                    if (configuredOrigins.Any(o =>
                            string.Equals(o, origin, StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }

                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    {
                        return false;
                    }

                    if (uri.Scheme is not ("http" or "https"))
                    {
                        return false;
                    }

                    // Local / loopback / IIS host names
                    if (uri.Host is "localhost" or "127.0.0.1" or "bagly.local")
                    {
                        return true;
                    }

                    // Allow Vercel / Netlify preview + production hosts
                    if (uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase) ||
                        uri.Host.EndsWith(".netlify.app", StringComparison.OrdinalIgnoreCase) ||
                        uri.Host.EndsWith(".onrender.com", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // Allow LAN + public IP origins (needed for IIS internet/LAN access)
                    return System.Net.IPAddress.TryParse(uri.Host, out _);
                })
                .AllowAnyHeader()
                .AllowAnyMethod()
                // SignalR needs credentialed requests; SetIsOriginAllowed (not AllowAnyOrigin) makes this safe.
                .AllowCredentials();
        });
    });

    var app = builder.Build();

    var emailOptions = app.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
    Log.Information(
        "Email startup: Enabled={Enabled}, Provider={Provider}, Configured={Configured}, HostSet={HostSet}, FromSet={FromSet}, SendGridKeySet={SendGridKeySet}, ResendKeySet={ResendKeySet}, Port={Port}, UseSsl={UseSsl}, WillSend={WillSend}",
        emailOptions.Enabled,
        emailOptions.ResolvedProvider,
        emailOptions.IsConfigured,
        emailOptions.HasSmtpHost,
        emailOptions.HasFromAddress,
        emailOptions.HasSendGridApiKey,
        emailOptions.HasResendApiKey,
        emailOptions.Port,
        emailOptions.UseSsl,
        emailOptions.WillSend);
    if (emailOptions.IsConfigured && !emailOptions.Enabled)
    {
        Log.Warning("Email is configured but Email__Enabled=false — order confirmation emails will not be sent.");
    }
    else if (!emailOptions.IsConfigured)
    {
        Log.Warning(
            "Email is not configured — set Email__Host + Email__FromAddress (SMTP) or Email__Provider=Resend + Email__ResendApiKey (HTTPS). Checkout succeeds but no confirmation email is sent.");
    }
    else if (emailOptions.UsesSmtpOnRenderFreeTier)
    {
        Log.Warning(
            "Email uses SMTP on Render. Free tier blocks outbound ports 25/465/587 — emails will time out. Set Email__Provider=Resend and Email__ResendApiKey (HTTPS), or upgrade Render to a paid instance.");
    }

    var shiprocketOptions = app.Configuration.GetSection(ShiprocketOptions.SectionName).Get<ShiprocketOptions>() ?? new ShiprocketOptions();
    Log.Information(
        "Shiprocket startup: Enabled={Enabled}, Configured={Configured}, EmailSet={EmailSet}, PasswordSet={PasswordSet}, PickupLocationSet={PickupLocationSet}",
        shiprocketOptions.Enabled,
        shiprocketOptions.IsConfigured,
        !string.IsNullOrWhiteSpace(shiprocketOptions.Email) && !shiprocketOptions.Email.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase),
        !string.IsNullOrWhiteSpace(shiprocketOptions.Password) && !shiprocketOptions.Password.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase),
        !string.IsNullOrWhiteSpace(shiprocketOptions.PickupLocation) && !shiprocketOptions.PickupLocation.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase));
    if (!shiprocketOptions.Enabled)
    {
        Log.Warning(
            "Shiprocket is disabled (Shiprocket__Enabled=false). Confirmed India orders will not be pushed to Shiprocket until Enabled=true and API credentials + pickup nickname are set.");
    }
    else if (!shiprocketOptions.IsConfigured)
    {
        Log.Warning(
            "Shiprocket__Enabled=true but Email/Password/PickupLocation are missing or still SET_VIA_ENV placeholders — shipment create will be skipped.");
    }

    try
    {
        await DatabaseBootstrapper.InitializeAsync(app.Services);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Database initialization failed. API will start; configure ConnectionStrings__DefaultConnection (Neon Postgres) and redeploy.");
    }

    var enableSwagger = app.Environment.IsDevelopment()
        || app.Configuration.GetValue("EnableSwagger", false);

    if (enableSwagger)
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    });

    app.UseCors("Frontend");

    if (app.Configuration.GetValue("EnableHttpsRedirection", false))
    {
        app.UseHttpsRedirection();
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<ExceptionLoggingMiddleware>();

    app.MapControllers();
    app.MapHub<ChatHub>("/hubs/chat");

    app.MapPost("/api/setup/seed", async (IServiceProvider sp) =>
    {
        var counts = await DatabaseBootstrapper.SeedOnlyAsync(sp);
        var admin = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Bagly.Api.Options.AdminOptions>>().Value;
        return Results.Ok(new
        {
            message = "Seed completed. Admin password synced from Admin__Password env (or default Admin@123).",
            categories = counts.Categories,
            products = counts.Products,
            admins = counts.Admins,
            adminEmail = admin.ResolveEmail(),
            adminPasswordConfigured = admin.IsPasswordConfigured,
        });
    });

    app.MapGet("/api/health", async (IConfiguration config, BaglyDbContext db, EmailDeliveryDiagnostics emailDiagnostics) =>
    {
        var cs = config.GetConnectionString("DefaultConnection") ?? "";
        var email = config.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
        var shiprocket = config.GetSection(ShiprocketOptions.SectionName).Get<ShiprocketOptions>() ?? new ShiprocketOptions();
        var lastEmailFailure = emailDiagnostics.GetLastFailure();
        var pickupNickname = shiprocket.IsConfigured ? shiprocket.PickupLocation.Trim() : null;
        var pickupLooksPlaceholder = !string.IsNullOrWhiteSpace(pickupNickname) &&
            string.Equals(pickupNickname, "test", StringComparison.OrdinalIgnoreCase);
        var openAi = config.GetSection(OpenAiOptions.SectionName).Get<OpenAiOptions>() ?? new OpenAiOptions();
        var chat = config.GetSection(ChatOptions.SectionName).Get<ChatOptions>() ?? new ChatOptions();
        var googleAuth = config.GetSection(GoogleAuthOptions.SectionName).Get<GoogleAuthOptions>() ?? new GoogleAuthOptions();
        var cloudinary = config.GetSection(CloudinaryOptions.SectionName).Get<CloudinaryOptions>() ?? new CloudinaryOptions();
        var storefront = config.GetSection(StorefrontOptions.SectionName).Get<StorefrontOptions>() ?? new StorefrontOptions();
        var corsOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var resolvedStorefrontBaseUrl = !string.IsNullOrWhiteSpace(storefront.BaseUrl)
            ? storefront.BaseUrl.Trim().TrimEnd('/')
            : corsOrigins.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o))?.Trim().TrimEnd('/');
        string? dataSource = null;
        try
        {
            var builderCs = new NpgsqlConnectionStringBuilder(cs);
            dataSource = builderCs.Host;
        }
        catch
        {
            dataSource = "(invalid connection string format)";
        }

        var looksLocal =
            !string.IsNullOrWhiteSpace(dataSource) &&
            (dataSource.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
             dataSource.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase));

        string dbStatus;
        string? dbError = null;
        try
        {
            // CanConnectAsync often returns false with no exception on network/auth failures.
            // Open a real connection so /api/health.error shows the actionable SQL message.
            await db.Database.OpenConnectionAsync();
            await db.Database.CloseConnectionAsync();
            dbStatus = "connected";
        }
        catch (Exception ex)
        {
            dbStatus = "unreachable";
            dbError = ex.GetBaseException().Message;
        }

        var connectionRelatedEnvKeys = Environment.GetEnvironmentVariables()
            .Keys
            .Cast<object>()
            .Select(k => k?.ToString() ?? "")
            .Where(k =>
                k.Contains("CONNECTION", StringComparison.OrdinalIgnoreCase) ||
                k.Contains("BAGLY", StringComparison.OrdinalIgnoreCase) ||
                k.Contains("SQL", StringComparison.OrdinalIgnoreCase) ||
                k.Contains("DATABASE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Results.Ok(new
        {
            status = dbStatus == "connected" ? "healthy" : "degraded",
            service = "Bagly.Api",
            database = new
            {
                status = dbStatus,
                dataSource,
                usingLocalPostgres = looksLocal,
                envVarsDetected = new
                {
                    BAGLY_CONNECTION_STRING = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BAGLY_CONNECTION_STRING")),
                    ConnectionStrings__DefaultConnection = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")),
                },
                connectionRelatedEnvKeys,
                hint = dbStatus != "connected"
                    ? "DB still unreachable from Render. Check: (1) the Neon project is active (not suspended), (2) BAGLY_CONNECTION_STRING is the Npgsql-format Neon connection string (Host=...;Database=...;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true), (3) the password has no stray quotes/whitespace, (4) Render → Manual Deploy after env changes. See database.error for the Postgres message."
                    : null,
                error = dbError,
            },
            logging = "Serilog",
            payments = "Razorpay (India)",
            email = new
            {
                enabled = email.Enabled,
                provider = email.ResolvedProvider.ToString(),
                configured = email.IsConfigured,
                willSend = email.WillSend,
                hostSet = email.HasSmtpHost,
                fromAddressSet = email.HasFromAddress,
                fromAddress = email.HasFromAddress ? email.FromAddress.Trim() : null,
                sendGridApiKeySet = email.HasSendGridApiKey,
                resendApiKeySet = email.HasResendApiKey,
                adminOrderNotifySet = !EmailOptions.IsPlaceholder(email.AdminOrderNotify),
                port = email.Port,
                useSsl = email.UseSsl,
                usernameSet = !string.IsNullOrWhiteSpace(email.Username) &&
                              !EmailOptions.IsPlaceholder(email.Username),
                passwordSet = !string.IsNullOrWhiteSpace(email.Password) &&
                              !EmailOptions.IsPlaceholder(email.Password),
                lastFailure = lastEmailFailure is null
                    ? null
                    : new
                    {
                        atUtc = lastEmailFailure.AtUtc,
                        provider = lastEmailFailure.Provider,
                        to = lastEmailFailure.ToMasked,
                        subject = lastEmailFailure.Subject,
                        statusCode = lastEmailFailure.StatusCode,
                        responseBody = lastEmailFailure.ResponseBody,
                    },
                hint = email.WillSend && email.UsesSmtpOnRenderFreeTier
                    ? "Render free tier blocks SMTP ports 25/465/587. Set Email__Provider=Resend and Email__ResendApiKey (HTTPS), or upgrade to a paid Render instance."
                    : email.WillSend && email.ResolvedProvider == EmailProvider.Resend
                        ? "If only alok73772@gmail.com (or your Resend signup email) receives order mail: bagly.co.in is not verified in Resend yet. Resend Domains → verify DNS → keep Email__FromAddress=noreply@bagly.co.in. Until Verified, Resend rejects every other recipient (admin notify to the signup email still works)."
                        : email.WillSend
                            ? null
                            : !email.IsConfigured
                                ? email.ResolvedProvider switch
                                {
                                    EmailProvider.SendGrid =>
                                        "Set Email__Provider=SendGrid, Email__SendGridApiKey, and Email__FromAddress (verified sender in SendGrid).",
                                    EmailProvider.Resend =>
                                        "Set Email__Provider=Resend, Email__ResendApiKey, and Email__FromAddress (verified domain in Resend, or onboarding@resend.dev for testing).",
                                    _ =>
                                        "Set Email__Host and Email__FromAddress on Render, then redeploy. Gmail: smtp.gmail.com, app password. HTTPS API (Render free): Email__Provider=Resend.",
                                }
                                : "Email__Enabled is false — set Email__Enabled=true to send order confirmation emails.",
            },
            shiprocket = new
            {
                enabled = shiprocket.Enabled,
                configured = shiprocket.IsConfigured,
                emailSet = !string.IsNullOrWhiteSpace(shiprocket.Email) &&
                           !shiprocket.Email.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase),
                passwordSet = !string.IsNullOrWhiteSpace(shiprocket.Password) &&
                              !shiprocket.Password.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase),
                pickupLocationSet = !string.IsNullOrWhiteSpace(shiprocket.PickupLocation) &&
                                    !shiprocket.PickupLocation.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase),
                pickupLocation = pickupNickname,
                baseUrl = string.IsNullOrWhiteSpace(shiprocket.BaseUrl)
                    ? "https://apiv2.shiprocket.in"
                    : shiprocket.BaseUrl.Trim().TrimEnd('/'),
                hint = !shiprocket.Enabled
                    ? "Shiprocket__Enabled is false — set Shiprocket__Enabled=true, Shiprocket__Email, Shiprocket__Password, and Shiprocket__PickupLocation (exact nickname from Shiprocket → Settings → Pickup Addresses)."
                    : !shiprocket.IsConfigured
                        ? "Shiprocket enabled but credentials/pickup incomplete. Set Shiprocket__Email (API user), Shiprocket__Password, Shiprocket__PickupLocation."
                        : pickupLooksPlaceholder
                            ? "Shiprocket__PickupLocation is 'test' — that almost never matches a real Shiprocket pickup nickname. Set it to the exact nickname from Shiprocket → Settings → Pickup Addresses, then place a new order. Admin → Orders shows shiprocketLastError when create fails."
                            : "After checkout, Admin → Orders should show shiprocketOrderId (or shiprocketLastError). Check Render logs for 'Shiprocket create failed' + response body.",
            },
            chat = new
            {
                hub = "/hubs/chat",
                mode = openAi.IsConfigured ? "openai" : "rule-based",
                aiConfigured = openAi.IsConfigured,
                model = openAi.IsConfigured ? openAi.Model : null,
                maxMessagesPerMinute = chat.MaxMessagesPerMinute,
                requiresAuth = true,
                hint = openAi.IsConfigured
                    ? null
                    : "Set OpenAi__ApiKey to enable the AI tool-calling agent. Chat still works via the rule-based fallback.",
            },
            customerAuth = new
            {
                googleConfigured = googleAuth.IsConfigured,
                hint = googleAuth.IsConfigured
                    ? null
                    : "Set GoogleAuth__ClientId on the backend to enable 'Continue with Google'.",
            },
            uploads = new
            {
                cloudinaryConfigured = cloudinary.IsConfigured,
                cloudName = cloudinary.HasCloudName ? cloudinary.CloudName : null,
                hint = cloudinary.IsConfigured
                    ? null
                    : "Set Cloudinary__CloudName, Cloudinary__ApiKey, and Cloudinary__ApiSecret to enable admin image uploads (free tier at cloudinary.com).",
            },
            stockAlerts = new
            {
                storefrontBaseUrl = resolvedStorefrontBaseUrl,
                hint = resolvedStorefrontBaseUrl is null
                    ? "Set Storefront__BaseUrl (or Cors__AllowedOrigins__0) so restock alert emails can link to the product page."
                    : null,
            },
            timestamp = DateTime.UtcNow,
        });
    });

    Log.Information("Bagly API starting (Npgsql/Postgres, console logging)");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Bagly API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static string? ResolveConnectionString(IConfiguration config)
{
    // Prefer explicit Render-friendly names first, then standard ASP.NET Core mapping.
    var candidates = new[]
    {
        Environment.GetEnvironmentVariable("BAGLY_CONNECTION_STRING"),
        Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"),
        Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__DEFAULTCONNECTION"),
        config["ConnectionStrings:DefaultConnection"],
        config.GetConnectionString("DefaultConnection"),
    };

    foreach (var raw in candidates)
    {
        var value = NormalizeConnectionString(raw);
        if (string.IsNullOrWhiteSpace(value))
        {
            continue;
        }

        // Ignore placeholders still sitting in appsettings.json
        if (value.Contains("ep-xxxx", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("YOUR_NEON", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        return ToNpgsqlKeywordConnectionString(value);
    }

    return null;
}

static string? NormalizeConnectionString(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    var trimmed = value.Trim();

    // Render UI sometimes stores values with accidental surrounding quotes.
    if ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) ||
        (trimmed.StartsWith('\'') && trimmed.EndsWith('\'')))
    {
        trimmed = trimmed[1..^1].Trim();
    }

    return trimmed;
}

// Neon (and most Postgres hosts) offer connection strings as a URI, e.g.
// postgresql://user:pass@ep-xxx.neon.tech/neondb?sslmode=require
// but Npgsql/EF Core expect the keyword=value format, e.g.
// Host=...;Database=...;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true
// NpgsqlConnectionStringBuilder cannot parse the URI form directly (it throws a
// confusing KeyNotFoundException/"invalid connection string format" error), so detect
// and convert it here — the single place every consumer (UseNpgsql, /api/health, the
// startup host-check log) reads the resolved connection string from.
static bool LooksLikePostgresUri(string value) =>
    value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
    value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

static string ToNpgsqlKeywordConnectionString(string value)
{
    if (!LooksLikePostgresUri(value))
    {
        return value;
    }

    try
    {
        return ConvertPostgresUriToNpgsqlConnectionString(value);
    }
    catch (Exception ex)
    {
        // Don't log `value` here — it contains the password.
        Log.Warning(ex, "Connection string looked like a postgres:// URI but could not be parsed as one. Falling back to the raw value.");
        return value;
    }
}

static string ConvertPostgresUriToNpgsqlConnectionString(string uriString)
{
    var uri = new Uri(uriString);

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
    };

    var database = uri.AbsolutePath.Trim('/');
    if (!string.IsNullOrWhiteSpace(database))
    {
        builder.Database = Uri.UnescapeDataString(database);
    }

    if (!string.IsNullOrWhiteSpace(uri.UserInfo))
    {
        var userParts = uri.UserInfo.Split(':', 2);
        builder.Username = Uri.UnescapeDataString(userParts[0]);
        if (userParts.Length > 1)
        {
            // Passwords are frequently URL-encoded (e.g. "@" -> "%40") inside the URI.
            builder.Password = Uri.UnescapeDataString(userParts[1]);
        }
    }

    var sslModeSet = false;
    if (!string.IsNullOrWhiteSpace(uri.Query))
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]);
            var val = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;

            if (string.Equals(key, "sslmode", StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<SslMode>(val, ignoreCase: true, out var parsedSslMode))
            {
                builder.SslMode = parsedSslMode;
                sslModeSet = true;
            }

            // Other Neon query params (e.g. channel_binding) aren't Npgsql keywords — ignore them
            // instead of letting NpgsqlConnectionStringBuilder throw on an unrecognized key.
        }
    }

    if (!sslModeSet)
    {
        // Neon (and most managed Postgres) require TLS; default to Require when the URI omits sslmode.
        builder.SslMode = SslMode.Require;
    }

    if (builder.SslMode is SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull)
    {
#pragma warning disable CS0618 // Obsolete in current Npgsql, but kept for parity with the keyword-format connection string documented in appsettings.json/health hints.
        builder.TrustServerCertificate = true;
#pragma warning restore CS0618
    }

    return builder.ConnectionString;
}
