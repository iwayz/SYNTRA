using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Syntra.Api.Auth;
using Syntra.Domain.Platform;
using Syntra.Domain.TenantData;

namespace Syntra.Api.Controllers;

public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token);
public record MeResponse(
    string TenantSlug,
    int TenantId,
    string? UserId,
    string? Email,
    IEnumerable<string> Roles,
    bool IsAdmin,
    IEnumerable<int> BranchIds);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty();
    }
}

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<TenantUser> _userManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUserAccess _userAccess;
    private readonly JwtTokenService _tokenService;

    public AuthController(
        UserManager<TenantUser> userManager,
        ICurrentTenant currentTenant,
        ICurrentUserAccess userAccess,
        JwtTokenService tokenService)
    {
        _userManager = userManager;
        _currentTenant = currentTenant;
        _userAccess = userAccess;
        _tokenService = tokenService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        // Same 401 for unknown user, wrong password and deactivated account,
        // so the response doesn't reveal which accounts exist.
        if (user is null || !user.IsActive || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized();
        }

        var roles = await _userManager.GetRolesAsync(user);
        var token = _tokenService.CreateToken(_currentTenant.TenantId, user, roles);

        return Ok(new LoginResponse(token));
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me()
    {
        // IsAdmin and BranchIds come from the database (current), not the
        // token; Roles is the token's snapshot and may lag behind.
        var access = await _userAccess.GetAsync();

        return Ok(new MeResponse(
            _currentTenant.Slug,
            _currentTenant.TenantId,
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value),
            access.IsAdmin,
            access.BranchIds));
    }

    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var access = await _userAccess.GetAsync();
        var user = await _userManager.FindByIdAsync(access.UserId.ToString());

        if (user is null)
        {
            return Unauthorized();
        }

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(error.Code, error.Description);
            }

            return ValidationProblem(ModelState);
        }

        return NoContent();
    }
}
