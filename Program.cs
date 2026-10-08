using Ideax.Data;
using Ideax.Entities;
using Ideax.Services.Token;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddScoped<Ideax.Filters.ApiResponseWrapperFilter>();
builder.Services.AddControllers(options =>
{
    options.Filters.AddService<Ideax.Filters.ApiResponseWrapperFilter>();
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        var comps = document.Components ?? new OpenApiComponents();
        comps.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        // API key scheme so Scalar sends the token as-is in the Authorization header
        // (no "Bearer " prefix required).
        comps.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = "Authorization",
            In = ParameterLocation.Header,
            Description = "Enter your JWT token here (do NOT include the 'Bearer ' prefix)."
        };

        document.Components = comps;

        return Task.CompletedTask;
    });
});

// Database and Identity configuration (PostgreSQL)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                      ?? builder.Configuration["ConnectionStrings:DefaultConnection"];

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddIdentity<User, IdentityRole<Guid>>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.User.RequireUniqueEmail = true;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Email service configuration
builder.Services.Configure<Ideax.Services.Email.EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddTransient<Ideax.Services.Email.IEmailSender, Ideax.Services.Email.MailKitEmailSender>();

// JWT configuration
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
var jwtSettings = new JwtSettings();
builder.Configuration.GetSection("Jwt").Bind(jwtSettings);
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddScoped<ITokenService, TokenService>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            ValidateLifetime = true
        };
    });

// Register Paystack API HttpClient (typed client)
builder.Services.AddHttpClient<Ideax.Services.Payments.PaystackApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Paystack:BaseUrl"] ?? "https://api.paystack.co/");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Enable Scalar API reference UI with Saturn (all-black dark theme)
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Idea X API Reference")
               .WithTheme(ScalarTheme.Saturn)
               .ForceDarkMode();
    });
}

app.UseHttpsRedirection();
// Middleware to normalize Authorization header: if client provides a raw JWT (no "Bearer " prefix)
// prepend "Bearer " so JwtBearer authentication will accept it.
app.Use(async (context, next) =>
{
    if (context.Request.Headers.TryGetValue("Authorization", out var values))
    {
        var first = values.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(first) && !first.Contains(' '))
        {
            // crude JWT check: contains two dots (header.payload.signature)
            if (first.Count(c => c == '.') == 2)
            {
                context.Request.Headers["Authorization"] = "Bearer " + first.Trim();
            }
        }
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Seed roles and default admin user at startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    var userManager = services.GetRequiredService<UserManager<User>>();
    var config = services.GetRequiredService<IConfiguration>();

    string[] roles = new[] { "Admin", "FrontEndEngineer", "ProductDesigner", "BackEndEngineer", "Client", "MobileEngineer" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }
    }

    var adminEmail = config["AdminUser:Email"];
    var adminPassword = config["AdminUser:Password"];
    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
    {
        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin == null)
        {
            admin = new User { UserName = adminEmail, Email = adminEmail, FullName = "Administrator", Role = UserRole.Admin };
            var create = await userManager.CreateAsync(admin, adminPassword);
            if (create.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, "Admin");
            }
        }
    }

    // Seed default services if missing
    var db = services.GetRequiredService<ApplicationDbContext>();
    var existing = await db.Services.Select(s => s.Name).ToListAsync();
    var seeds = new List<Service>
    {
        new Service { Name = "Frontend", Description = "Frontend development (React, Angular, Vue)", Category = ServiceCategory.SoftwareEngineering, Price = 180_000m },
        new Service { Name = "Back end", Description = "Backend development (APIs, Databases)", Category = ServiceCategory.SoftwareEngineering, Price = 200_000m },
        new Service { Name = "Product design", Description = "Product design and strategy", Category = ServiceCategory.ProductDesign, Price = 300_000m },
        new Service { Name = "MobileApplication", Description = "Mobile application development (iOS/Android)", Category = ServiceCategory.SoftwareEngineering, Price = 350_000m }
    };

    foreach (var s in seeds)
    {
        if (!existing.Contains(s.Name))
        {
            db.Services.Add(s);
        }
    }

    await db.SaveChangesAsync();
}

app.Run();