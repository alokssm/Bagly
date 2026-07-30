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

    var connectionString = ResolveConnectionString(builder.Configuration);
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Database connection string is missing. On Render set env var ConnectionStrings__DefaultConnection (or BAGLY_CONNECTION_STRING) to your Azure SQL ADO.NET string.");
    }

    try
    {
        var ds = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString).DataSource;
        Log.Information("Using SQL data source: {DataSource}", ds);
        if (ds.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            ds.Contains("SQLEXPRESS", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("SQL data source looks local ({DataSource}). Render cannot reach your PC SQL Express. Set Azure SQL connection string in Render env vars.", ds);
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
    builder.Services.AddSingleton<TokenService>();
    builder.Services.AddScoped<IAuditLogService, AuditLogService>();
    builder.Services.AddScoped<IPaymentLogService, PaymentLogService>();
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

    app.MapGet("/api/health", async (IConfiguration config, BaglyDbContext db) =>
    {
        var cs = config.GetConnectionString("DefaultConnection") ?? "";
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

        return Results.Ok(new
        {
            status = dbStatus == "connected" ? "healthy" : "degraded",
            service = "Bagly.Api",
            database = new
            {
                status = dbStatus,
                dataSource,
                usingLocalSqlExpress = looksLocal,
                hint = looksLocal
                    ? "Render is still using local SQL Express. Set ConnectionStrings__DefaultConnection to your Azure SQL ADO.NET string and redeploy."
                    : null,
                error = dbError,
            },
            logging = "Serilog",
            payments = "Razorpay (India)",
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
