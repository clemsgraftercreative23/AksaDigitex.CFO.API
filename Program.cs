using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MyBackend.Application.Auth;
using MyBackend.Application.Options;
using MyBackend.Application.Services;
using MyBackend.Features.Auth;
using MyBackend.Features.Coa;
using MyBackend.Features.Companies;
using MyBackend.Features.LaporanKeuangan;
using MyBackend.Features.Lookup;
using MyBackend.Features.Entitas;
using MyBackend.Features.Root;
using MyBackend.Features.SalesOrders;
using MyBackend.Features.UtangPiutang;
using MyBackend.Features.Users;
using MyBackend.Infrastructure.Clients;
using MyBackend.Infrastructure.Persistence;
using MyBackend.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Swashbuckle v10: OpenApiSecuritySchemeReference must be (schemeId, document) or Swagger UI omits Authorization.
    c.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description =
            "JWT dari POST /api/auth/login (field accessToken). Tempel hanya string JWT-nya — tanpa kata \"Bearer\" (Swagger menambahkannya).",
    });
    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = [],
    });
});

var connectionString = builder.Configuration.GetConnectionString("Cfo");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Cfo' is missing. When ASPNETCORE_ENVIRONMENT is Development, set ConnectionStrings:Cfo in appsettings.Development.json. " +
        "Otherwise set the environment variable ConnectionStrings__Cfo or use: dotnet user-secrets set ConnectionStrings:Cfo \"Host=...;Port=...;Database=cfo;Username=...;Password=...\"");
}

builder.Services.AddDbContext<CfoDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
        // Mitigasi kasus koneksi diputus paksa (transient) saat baca dari DB.
        // Npgsql/EF akan retry beberapa kali sebelum gagal.
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null)));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));
builder.Services.Configure<CompanyAliasOptions>(builder.Configuration.GetSection(CompanyAliasOptions.SectionName));

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must be set and at least 32 characters long.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            ValidateIssuer = true,
            ValidateAudience = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthConstants.SuperDuperAdminPolicy, policy =>
        policy.RequireRole(AuthConstants.SuperDuperAdminRole));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 20;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

builder.Services.AddHttpClient<AccurateHttpClient>();
builder.Services.AddScoped<IAccurateService, AccurateService>();
builder.Services.AddSingleton<IPasswordHasherService, BCryptPasswordHasherService>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IAccurateCompanyKeyResolver, AccurateCompanyKeyResolver>();
builder.Services.AddScoped<ICompanyAccessService, CompanyAccessService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors("AllowAll");

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI();

app.MapRootEndpoints();
app.MapAuthEndpoints();
app.MapUsersEndpoints();
app.MapLookupEndpoints();
app.MapCompaniesEndpoints();
app.MapCoaEndpoints();
app.MapUtangPiutangEndpoints();
app.MapEntitasEndpoints();
app.MapLaporanKeuanganEndpoints();
app.MapSalesOrdersEndpoints();

app.Run();
