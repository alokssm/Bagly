using System.Text;
using Bagly.Api.Data;
using Bagly.Api.Middleware;
using Bagly.Api.Options;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;

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
            "Database connection string is missing. In Render → Environment add KEY exactly 'BAGLY_CONNECTION_STRING' (or 'ConnectionStrings__DefaultConnection') with your Azure SQL ADO.NET value, then Manual Deploy.");
        // Keep process alive so /api/health can report what's missing.
        connectionString =
            "Server=127.0.0.1,1433;Database=missing;User ID=missing;Password=missing;Encrypt=False;TrustServerCertificate=True;Connection Timeout=1;";
    }

    try
    {
        var ds = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString).DataSource;
        Log.Information("Using SQL data source: {DataSource}", ds);
        if (ds.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            ds.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            ds.Contains("SQLEXPRESS", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("SQL data source looks local/placeholder ({DataSource}). Set Azure SQL on Render env vars and redeploy.", ds);
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

    var sqlLoggingEnabled = !string.IsNullOrWhiteSpace(connectionString)
        && connectionString.Contains("database.windows.net", StringComparison.OrdinalIgnoreCase);

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

        if (!sqlLoggingEnabled)
        {
            return;
        }

        var sinkOptions = new MSSqlServerSinkOptions
        {
            TableName = "Logs",
            SchemaName = "dbo",
            AutoCreateSqlTable = true,
            BatchPostingLimit = 50,
            BatchPeriod = TimeSpan.FromSeconds(2),
        };

        var columnOptions = new ColumnOptions();
        columnOptions.Store.Remove(StandardColumn.Properties);
        columnOptions.Store.Add(StandardColumn.LogEvent);
        columnOptions.AdditionalColumns =
        [
            new SqlColumn("RequestPath", System.Data.SqlDbType.NVarChar, dataLength: 500),
            new SqlColumn("ActorEmail", System.Data.SqlDbType.NVarChar, dataLength: 256),
            new SqlColumn("AuditCategory", System.Data.SqlDbType.NVarChar, dataLength: 50),
            new SqlColumn("AuditAction", System.Data.SqlDbType.NVarChar, dataLength: 100),
        ];

        configuration.WriteTo.MSSqlServer(
            connectionString: connectionString,
            sinkOptions: sinkOptions,
            columnOptions: columnOptions);
    });

    builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
    builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
    builder.Services.Configure<RazorpayOptions>(builder.Configuration.GetSection(RazorpayOptions.SectionName));
    builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
    builder.Services.AddSingleton<TokenService>();
    builder.Services.AddScoped<IAuditLogService, AuditLogService>();
    builder.Services.AddScoped<IPaymentLogService, PaymentLogService>();
    builder.Services.AddScoped<IOrderConfirmationEmailService, OrderConfirmationEmailService>();
    builder.Services.AddSingleton<IOrderConfirmationEmailDispatcher, OrderConfirmationEmailDispatcher>();
    builder.Services.AddHostedService(sp => (OrderConfirmationEmailDispatcher)sp.GetRequiredService<IOrderConfirmationEmailDispatcher>());
    builder.Services.AddHttpClient("SendGrid", client =>
    {
        client.BaseAddress = new Uri("https://api.sendgrid.com/");
        client.Timeout = TimeSpan.FromSeconds(30);
    });
    builder.Services.AddHttpClient<IRazorpayService, RazorpayService>();

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
        });

    builder.Services.AddAuthorization();

    builder.Services.AddDbContext<BaglyDbContext>(options =>
        options.UseSqlServer(connectionString));

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
                .AllowAnyMethod();
        });
    });

    var app = builder.Build();

    var emailOptions = app.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
    Log.Information(
        "Email startup: Enabled={Enabled}, Provider={Provider}, Configured={Configured}, HostSet={HostSet}, FromSet={FromSet}, SendGridKeySet={SendGridKeySet}, Port={Port}, UseSsl={UseSsl}, WillSend={WillSend}",
        emailOptions.Enabled,
        emailOptions.ResolvedProvider,
        emailOptions.IsConfigured,
        emailOptions.HasSmtpHost,
        emailOptions.HasFromAddress,
        emailOptions.HasSendGridApiKey,
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
            "Email is not configured — set Email__Host + Email__FromAddress (SMTP) or Email__Provider=SendGrid + Email__SendGridApiKey. Checkout succeeds but no confirmation email is sent.");
    }
    else if (emailOptions.UsesSmtpOnRenderFreeTier)
    {
        Log.Warning(
            "Email uses SMTP on Render. Free tier blocks outbound ports 25/465/587 — emails will time out. Set Email__Provider=SendGrid and Email__SendGridApiKey (HTTPS), or upgrade Render to a paid instance.");
    }

    try
    {
        await DatabaseBootstrapper.InitializeAsync(app.Services);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Database initialization failed. API will start; configure ConnectionStrings__DefaultConnection (Azure SQL) and redeploy.");
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

    app.MapGet("/api/health", async (IConfiguration config, BaglyDbContext db) =>
    {
        var cs = config.GetConnectionString("DefaultConnection") ?? "";
        var email = config.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
        string? dataSource = null;
        try
        {
            var builderCs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cs);
            dataSource = builderCs.DataSource;
        }
        catch
        {
            dataSource = "(invalid connection string format)";
        }

        var looksLocal =
            !string.IsNullOrWhiteSpace(dataSource) &&
            (dataSource.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
             dataSource.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
             dataSource.Contains("SQLEXPRESS", StringComparison.OrdinalIgnoreCase) ||
             dataSource.Contains(".\\", StringComparison.OrdinalIgnoreCase));

        string dbStatus;
        string? dbError = null;
        try
        {
            var canConnect = await db.Database.CanConnectAsync();
            dbStatus = canConnect ? "connected" : "unreachable";
        }
        catch (Exception ex)
        {
            dbStatus = "error";
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
                usingLocalSqlExpress = looksLocal,
                envVarsDetected = new
                {
                    BAGLY_CONNECTION_STRING = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BAGLY_CONNECTION_STRING")),
                    ConnectionStrings__DefaultConnection = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")),
                },
                connectionRelatedEnvKeys,
                hint = looksLocal || dbStatus != "connected"
                    ? "Azure firewall alone is not enough. Render must inject BAGLY_CONNECTION_STRING into THIS web service. Check Environment keys listed in connectionRelatedEnvKeys, then Save + Manual Deploy."
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
                sendGridApiKeySet = email.HasSendGridApiKey,
                port = email.Port,
                useSsl = email.UseSsl,
                usernameSet = !string.IsNullOrWhiteSpace(email.Username) &&
                              !EmailOptions.IsPlaceholder(email.Username),
                passwordSet = !string.IsNullOrWhiteSpace(email.Password) &&
                              !EmailOptions.IsPlaceholder(email.Password),
                hint = email.WillSend && email.UsesSmtpOnRenderFreeTier
                    ? "Render free tier blocks SMTP ports 25/465/587. Set Email__Provider=SendGrid and Email__SendGridApiKey (HTTPS), or upgrade to a paid Render instance."
                    : email.WillSend
                        ? null
                        : !email.IsConfigured
                            ? email.ResolvedProvider == EmailProvider.SendGrid
                                ? "Set Email__Provider=SendGrid, Email__SendGridApiKey, and Email__FromAddress (verified sender in SendGrid)."
                                : "Set Email__Host and Email__FromAddress on Render, then redeploy. Gmail: smtp.gmail.com, app password. SendGrid API (Render free): Email__Provider=SendGrid."
                            : "Email__Enabled is false — set Email__Enabled=true to send order confirmation emails.",
            },
            timestamp = DateTime.UtcNow,
        });
    });

    Log.Information("Bagly API starting with Serilog SQL Server logging");
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
        if (value.Contains("YOUR_SERVER", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("YOUR_ADMIN", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("YOUR_PASSWORD", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("SET_VIA_ENV_", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        return value;
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
