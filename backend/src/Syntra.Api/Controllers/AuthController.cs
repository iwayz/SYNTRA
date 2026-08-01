using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Syntra.Api.Auth;
using Syntra.Domain.Platform;
using Syntra.Domain.TenantData;

namespace Syntra.Api.Controllers;

public record LoginRequest(string Email, string Password);

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<TenantUser> _userManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly JwtTokenService _tokenService;

    public AuthController(UserManager<TenantUser> userManager, ICurrentTenant currentTenant, JwtTokenService tokenService)
    {
        _userManager = userManager;
        _currentTenant = currentTenant;
        _tokenService = tokenService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized();
        }

        var roles = await _userManager.GetRolesAsync(user);
        var token = _tokenService.CreateToken(_currentTenant.TenantId, user, roles);

        return Ok(new { token });
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        return Ok(new
        {
            tenantSlug = _currentTenant.Slug,
            tenantId = _currentTenant.TenantId,
            userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value)
        });
    }
}
