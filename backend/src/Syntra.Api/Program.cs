using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Syntra.Api.Auth;
using Syntra.Api.Filters;
using Syntra.Api.Middleware;
using Syntra.Domain.Platform;
using Syntra.Domain.TenantData;
using Syntra.Infrastructure.Platform;
using Syntra.Infrastructure.TenantData;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<PlatformDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PlatformDb")));

builder.Services.AddScoped<CurrentTenant>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());

// TenantDbContext's connection string is resolved per-request from
// ICurrentTenant, which TenantResolutionMiddleware populates early in the
// pipeline - never a fixed connection string set once at startup.
builder.Services.AddDbContext<TenantDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<ICurrentTenant>().ConnectionString));

builder.Services.AddIdentityCore<TenantUser>()
    .AddRoles<TenantRole>()
    .AddEntityFrameworkStores<TenantDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"]!))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseMiddleware<TenantResolutionMiddleware>();

app.UseAuthentication();

app.UseMiddleware<TenantClaimGuardMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Top-level statements generate an internal Program class by default; make it
// public so Syntra.Tests can reference it via WebApplicationFactory<Program>.
public partial class Program
{
}
